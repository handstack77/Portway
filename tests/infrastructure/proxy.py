import base64, select, socket, socketserver, struct

def relay(a, b):
    try:
        while True:
            ready, _, _ = select.select([a, b], [], [], 30)
            if not ready: return
            for src in ready:
                data = src.recv(65536)
                if not data: return
                (b if src is a else a).sendall(data)
    finally:
        b.close()

def readn(sock, count):
    data = b''
    while len(data) < count:
        chunk = sock.recv(count - len(data))
        if not chunk: raise EOFError()
        data += chunk
    return data

def destination(host, port):
    if host not in ('127.0.0.1', 'localhost') or port not in (22, 2121, 2122, 8081, 8443, 5000):
        raise ValueError('Fixture destinations only')
    return socket.create_connection(('127.0.0.1', port), timeout=10)

class Proxy(socketserver.BaseRequestHandler):
    def handle(self):
        sock = self.request
        first = readn(sock, 1)
        if first == b'\x05':
            readn(sock, readn(sock, 1)[0]); sock.sendall(b'\x05\x00')
            header = readn(sock, 4)
            host = socket.inet_ntoa(readn(sock, 4)) if header[3] == 1 else readn(sock, readn(sock, 1)[0]).decode()
            port = struct.unpack('!H', readn(sock, 2))[0]
            upstream = destination(host, port)
            sock.sendall(b'\x05\x00\x00\x01\x7f\x00\x00\x01\x00\x00')
        elif first == b'\x04':
            header = readn(sock, 7); port = struct.unpack('!H', header[1:3])[0]; addr = header[3:7]
            while readn(sock, 1) != b'\x00': pass
            if addr[:3] == b'\x00\x00\x00' and addr[3] != 0:
                value = bytearray()
                while (b := readn(sock, 1)) != b'\x00': value.extend(b)
                host = value.decode()
            else: host = socket.inet_ntoa(addr)
            upstream = destination(host, port); sock.sendall(b'\x00\x5a' + header[1:7])
        else:
            data = first
            while not data.endswith(b'\r\n\r\n') and len(data) < 8192: data += readn(sock, 1)
            line = data.decode().split('\r\n')[0]
            method, target, _ = line.split(' ')
            if method != 'CONNECT': return
            host, port = target.rsplit(':', 1); upstream = destination(host, int(port))
            sock.sendall(b'HTTP/1.1 200 Connection established\r\n\r\n')
        relay(sock, upstream)

class Server(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True

if __name__ == '__main__': Server(('0.0.0.0', 8088), Proxy).serve_forever()
