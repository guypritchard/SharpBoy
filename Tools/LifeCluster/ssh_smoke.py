"""Exercise the real OpenSSH client, exec channel and reverse forwards on loopback.

Uses an ephemeral, key-authenticated test SSH server; no services, user SSH files,
firewall rules or production hosts are changed. Worker computation is the real ROM.
Run after building GB.Experiments in Release. This tests transport, not physical LANs.
"""
import json
import os
import secrets
import pathlib
import socket
import subprocess
import threading
import time

import paramiko


ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / "artifacts" / "life-cluster" / "ssh-smoke"
WORKER = ROOT / "GB.Experiments/bin/Release/net10.0/GB.Experiments"
if os.name == "nt":
    WORKER = pathlib.Path(str(WORKER) + ".exe")


def background(function, *args):
    thread = threading.Thread(target=function, args=args, daemon=True)
    thread.start()
    return thread


def copy_bytes(read, write, finish):
    try:
        while data := read(32768):
            write(data)
    except (OSError, EOFError):
        pass
    finally:
        try:
            finish()
        except OSError:
            pass


class Server(paramiko.ServerInterface):
    def __init__(self, transport, client_key, children, password):
        self.transport, self.client_key, self.children = transport, client_key, children
        self.password = password
        self.listeners = []
        self.commands = []

    def get_allowed_auths(self, username):
        return "publickey,password"

    def check_auth_password(self, username, password):
        return paramiko.AUTH_SUCCESSFUL if username == "life-test" and password == self.password else paramiko.AUTH_FAILED

    def check_auth_publickey(self, username, key):
        return paramiko.AUTH_SUCCESSFUL if username == "life-test" and key == self.client_key else paramiko.AUTH_FAILED

    def check_channel_request(self, kind, chanid):
        return paramiko.OPEN_SUCCEEDED if kind == "session" else paramiko.OPEN_FAILED_ADMINISTRATIVELY_PROHIBITED

    def check_port_forward_request(self, address, port):
        if address != "127.0.0.1":
            return False
        listener = socket.socket()
        try:
            listener.bind((address, port))
            listener.listen()
            listener.settimeout(0.2)
        except OSError:
            listener.close()
            return False
        self.listeners.append(listener)
        background(self.forward, listener)
        return port

    def forward(self, listener):
        while self.transport.is_active():
            try:
                peer, origin = listener.accept()
            except socket.timeout:
                continue
            except OSError:
                return
            try:
                channel = self.transport.open_forwarded_tcpip_channel(origin, listener.getsockname())
                background(copy_bytes, peer.recv, channel.sendall, channel.shutdown_write)
                background(copy_bytes, channel.recv, peer.sendall, peer.close)
            except (OSError, paramiko.SSHException):
                peer.close()

    def check_channel_exec_request(self, channel, command):
        expected = f"{WORKER.as_posix()} --life-worker-group"
        if command.decode().replace("\\", "/") != expected:
            return False
        self.commands.append(command)
        background(self.execute, channel)
        return True

    def execute(self, channel):
        with subprocess.Popen([str(WORKER), "--life-worker-group"], stdin=subprocess.PIPE,
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE) as process:
            self.children.append(process)
            background(copy_bytes, channel.recv, lambda b: (process.stdin.write(b), process.stdin.flush()), process.stdin.close)
            output = background(copy_bytes, process.stdout.read1, channel.sendall, lambda: None)
            error = background(copy_bytes, process.stderr.read1, channel.sendall_stderr, lambda: None)
            try:
                process.wait(timeout=40)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()
            output.join(3)
            error.join(3)
            for listener in self.listeners:
                listener.close()
            try:
                channel.send_exit_status(process.returncode)
                channel.close()
            except OSError:
                pass


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    paramiko.util.log_to_file(str(OUT / "ssh.log"))
    host_key, client_key = paramiko.RSAKey.generate(2048), paramiko.RSAKey.generate(2048)
    password = secrets.token_hex(32)
    client_key.write_private_key_file(str(OUT / "identity"))
    if os.name == "nt":
        user = subprocess.check_output(["whoami"], text=True).strip()
        subprocess.run(["icacls", str(OUT / "identity"), "/inheritance:r", "/grant:r", f"{user}:(F)"], check=True, capture_output=True)
    else:
        (OUT / "identity").chmod(0o600)
    listener = socket.socket()
    listener.bind(("127.0.0.1", 0))
    listener.listen()
    listener.settimeout(0.2)
    port = listener.getsockname()[1]
    (OUT / "known_hosts").write_text(f"[127.0.0.1]:{port} {host_key.get_name()} {host_key.get_base64()}\n")
    config = OUT / "ssh.config"
    config.write_text(f"""Host life-a life-b
    HostName 127.0.0.1
    Port {port}
    User life-test
    IdentityFile { (OUT / 'identity').as_posix() }
    UserKnownHostsFile { (OUT / 'known_hosts').as_posix() }
    IdentitiesOnly yes
    LogLevel ERROR
""")
    transports, servers, children = [], [], []
    stop = threading.Event()

    def accept():
        while not stop.is_set():
            try:
                peer, _ = listener.accept()
            except socket.timeout:
                continue
            transport = paramiko.Transport(peer)
            transport.add_server_key(host_key)
            server = Server(transport, client_key, children, password)
            transports.append(transport)
            servers.append(server)
            transport.start_server(server=server)
            def hold_sessions(active):
                channels = []
                while active.is_active():
                    channel = active.accept(0.2)
                    if channel is not None:
                        channels.append(channel)
            background(hold_sessions, transport)

    thread = background(accept)
    # Reserve distinct contiguous ranges for the two simulated remote hosts.
    reserved = []
    try:
        for _ in range(4):
            sock = socket.socket()
            sock.bind(("127.0.0.1", 0))
            reserved.append(sock)
        # One node per SSH process allows ephemeral ports rather than guessing free ranges.
        hosts = [{"Nodes": 2}]
        for index, sock in enumerate(reserved):
            hosts.append({"Destination": "life-a" if index < 2 else "life-b", "Executable": WORKER.as_posix(),
                          "Nodes": 1, "FirstPort": sock.getsockname()[1]})
        placement = OUT / "hosts.json"
        placement.write_text(json.dumps({"Hosts": hosts, "SshConfigFile": str(config)}))
    finally:
        for sock in reserved:
            sock.close()
    command = [str(WORKER), "--life", "--columns", "3", "--rows", "2", "--generations", "3", "--tick-delay-ms", "0"]
    try:
        password_placement = OUT / "password-hosts.json"
        password_placement.write_text(json.dumps({"Hosts": hosts, "SshConfigFile": str(config), "PasswordEnvironmentVariable": "LIFE_SMOKE_PASSWORD"}))
        env = os.environ.copy()
        env["LIFE_SMOKE_PASSWORD"] = password
        for name, extra in [("local", []), ("distributed", ["--hosts", str(placement)]), ("password", ["--hosts", str(password_placement)])]:
            run = subprocess.run(command + extra + ["--report", str(OUT / f"{name}.json")],
                                 capture_output=True, text=True, timeout=60, env=env)
            print(run.stdout, end="")
            if run.returncode:
                raise AssertionError(run.stderr)
        local = json.loads((OUT / "local.json").read_text(encoding="utf-8-sig"))
        remote = json.loads((OUT / "distributed.json").read_text(encoding="utf-8-sig"))
        assert local["WorldSha256"] == remote["WorldSha256"]
        assert len(remote["Nodes"]) == 6
        authenticated = json.loads((OUT / "password.json").read_text(encoding="utf-8-sig"))
        assert authenticated["WorldSha256"] == local["WorldSha256"]
        assert len(children) == 8 and sum(len(s.commands) for s in servers) == 8
        assert sum(x["Remote"] for x in remote["Metrics"]["Locations"]) == 4
        assert all(x["ProcessId"] > 0 for x in remote["Metrics"]["Locations"])
        assert all(p.poll() == 0 for p in children), "SSH worker processes were not reaped"
        print("PASS: six native nodes, two SSH aliases plus local placement; encrypted reverse forwards; identical world SHA-256.")
        print("PASS: noninteractive password authentication through the apphost askpass helper; no credential arguments or host JSON passwords.")

        # A lost SSH connection must fail the barrier, rather than silently replacing a territory.
        failure_command = command.copy()
        failure_command[failure_command.index("--generations") + 1] = "100"
        failure_command[failure_command.index("--tick-delay-ms") + 1] = "50"
        before = len(children)
        with subprocess.Popen(failure_command + ["--hosts", str(placement)], stdout=subprocess.PIPE,
                              stderr=subprocess.PIPE, text=True) as process:
            deadline = threading.Timer(25, process.kill)
            deadline.start()
            dropped = False
            try:
                for line in process.stdout:
                    if line.startswith("Generation 1:") and not dropped:
                        next(t for t in transports if t.is_active()).close()
                        dropped = True
                error = process.stderr.read()
                process.wait(timeout=10)
                assert dropped and process.returncode == 2 and "Life worker failed" in error, error
            finally:
                deadline.cancel()
        deadline = time.monotonic() + 8
        while any(p.poll() is None for p in children[before:]) and time.monotonic() < deadline:
            time.sleep(0.05)
        assert all(p.poll() is not None for p in children[before:]), "Disconnected SSH workers survived cancellation"
        print("PASS: lost SSH transport fails the generation barrier and stops every remote worker.")
    finally:
        stop.set()
        for transport in transports:
            transport.close()
        for server in servers:
            for sock in server.listeners:
                sock.close()
        thread.join(2)
        listener.close()
        for process in children:
            if process.poll() is None:
                try:
                    process.wait(timeout=8)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()


if __name__ == "__main__":
    main()
