FROM python:3.12-slim
RUN apt-get update && apt-get install -y --no-install-recommends openssh-server openssl putty-tools socat && rm -rf /var/lib/apt/lists/*
RUN pip install --no-cache-dir pyftpdlib==2.1.0 WsgiDAV==4.3.3 cheroot==10.0.1 'moto[server]==5.1.5' boto3==1.37.38
RUN pip install --no-cache-dir pyOpenSSL==25.1.0
RUN useradd -m -s /bin/bash portway && echo 'portway:portway-test-only' | chpasswd && mkdir -p /run/sshd /home/portway/files && chown portway:portway /home/portway/files
COPY servers.py /servers.py
COPY proxy.py /proxy.py
CMD ["python", "/servers.py"]
