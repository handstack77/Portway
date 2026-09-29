"""Isolated protocol fixtures. Only publish these container ports on loopback."""
import os, pathlib, subprocess, threading, time
from pyftpdlib.authorizers import DummyAuthorizer
from pyftpdlib.handlers import FTPHandler, TLS_FTPHandler
from pyftpdlib.servers import FTPServer
from pyftpdlib.ioloop import IOLoop
from wsgidav.wsgidav_app import WsgiDAVApp
from cheroot import wsgi
from moto.server import ThreadedMotoServer
import boto3
from cheroot.ssl.builtin import BuiltinSSLAdapter

subprocess.run(['ssh-keygen', '-A'], check=True)
# 호스트 키 거부 테스트는 인증 전에 의도적으로 여러 번 연결을 끊습니다.
# 격리된 루프백 테스트 서버는 다른 테스트에 접속 출발지 제한을 적용하면 안 됩니다.
subprocess.Popen(['/usr/sbin/sshd', '-D', '-e', '-o', 'KbdInteractiveAuthentication=yes', '-o', 'UsePAM=yes', '-o', 'PerSourcePenalties=no', '-o', 'MaxStartups=100:30:200'])
root = pathlib.Path('/fixtures'); root.mkdir(exist_ok=True)
for protocol in ('ftp', 'dav'):
    (root / protocol).mkdir(exist_ok=True)
keys = pathlib.Path('/home/portway/keys'); keys.mkdir(exist_ok=True)
if not (keys / 'id_ed25519').exists():
    subprocess.run(['ssh-keygen','-t','ed25519','-N','','-f',str(keys / 'id_ed25519')], check=True)
    subprocess.run(['puttygen',str(keys / 'id_ed25519'),'-O','private','-o',str(keys / 'id_ed25519.ppk')], check=True)
sshdir = pathlib.Path('/home/portway/.ssh'); sshdir.mkdir(exist_ok=True)
(sshdir / 'authorized_keys').write_text((keys / 'id_ed25519.pub').read_text())
subprocess.run(['chown','-R','portway:portway',str(keys),str(sshdir)], check=True)
sshdir.chmod(0o700); (sshdir / 'authorized_keys').chmod(0o600)
os.environ['SSH_AUTH_SOCK'] = '/fixtures/agent.sock'
subprocess.Popen(['ssh-agent','-D','-a',os.environ['SSH_AUTH_SOCK']])
for _ in range(50):
    if pathlib.Path(os.environ['SSH_AUTH_SOCK']).exists(): break
    time.sleep(.1)
subprocess.run(['ssh-add',str(keys / 'id_ed25519')],check=True)
subprocess.Popen(['socat','TCP-LISTEN:3022,reuseaddr,fork','UNIX-CONNECT:/fixtures/agent.sock'])
subprocess.Popen(['python','/proxy.py'])
cert = '/fixtures/cert.pem'; key = '/fixtures/key.pem'
subprocess.run(['openssl','req','-x509','-newkey','rsa:2048','-nodes','-keyout',key,'-out',cert,'-days','2','-subj','/CN=localhost','-addext','subjectAltName=DNS:localhost,IP:127.0.0.1'],check=True)
auth = DummyAuthorizer()
auth.add_user('portway', 'portway-test-only', str(root / 'ftp'), perm='elradfmwMT')
FTPHandler.authorizer = auth
FTPHandler.passive_ports = range(30000, 30011)
ftp = FTPServer(('0.0.0.0', 2121), FTPHandler, ioloop=IOLoop())
threading.Thread(target=ftp.serve_forever, daemon=True).start()
TLS_FTPHandler.authorizer = auth
TLS_FTPHandler.certfile = cert; TLS_FTPHandler.keyfile = key
TLS_FTPHandler.tls_control_required = True; TLS_FTPHandler.tls_data_required = True
TLS_FTPHandler.passive_ports = range(30011,30021)
secure_ftp = FTPServer(('0.0.0.0',2122),TLS_FTPHandler,ioloop=IOLoop())
threading.Thread(target=secure_ftp.serve_forever, daemon=True).start()
app = WsgiDAVApp({'provider_mapping': {'/': str(root / 'dav')}, 'simple_dc': {'user_mapping': {'*': {'portway': {'password': 'portway-test-only'}}}}, 'http_authenticator': {'accept_basic': True, 'accept_digest': False, 'default_to_digest': False}, 'verbose': 1})
dav = wsgi.Server(('0.0.0.0', 8081), app)
threading.Thread(target=dav.start, daemon=True).start()
secure_dav = wsgi.Server(('0.0.0.0',8443),app)
secure_dav.ssl_adapter = BuiltinSSLAdapter(cert,key)
threading.Thread(target=secure_dav.start, daemon=True).start()
moto = ThreadedMotoServer(ip_address='0.0.0.0', port=5000, verbose=False)
moto.start()
s3 = boto3.client('s3', endpoint_url='http://127.0.0.1:5000', aws_access_key_id='test', aws_secret_access_key='test', region_name='us-east-1')
s3.create_bucket(Bucket='portway-tests')
print('PORTWAY 테스트 서버 준비 완료', flush=True)
threading.Event().wait()
