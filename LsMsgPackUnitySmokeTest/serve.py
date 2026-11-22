#!/usr/bin/env python3
"""Serves a WebGL build of the smoke test, or only golden.txt for an Android player (through adb reverse), for one run (run.sh): GET /golden.txt is the golden file, POST /result (the player's report, WebRunner.cs)
is written to the output file and stops the server; the exit code is the player's (1: failures, 3: no report before the timeout).
Usage: serve.py <build folder> <golden.txt> <output.txt> <port file> [timeout seconds]"""
import functools, http.server, sys, threading, urllib.parse

build, golden, output, port_file = sys.argv[1:5]
timeout = float(sys.argv[5]) if len(sys.argv) > 5 else 600
result = {'code': 3}


class Handler(http.server.SimpleHTTPRequestHandler):
    def do_GET(self):
        if urllib.parse.urlparse(self.path).path == '/golden.txt':
            with open(golden, 'rb') as f:
                body = f.read()
            self.send_response(200)
            self.send_header('Content-Type', 'text/plain; charset=utf-8')
            self.send_header('Content-Length', str(len(body)))
            self.end_headers()
            self.wfile.write(body)
        else:
            super().do_GET()

    def do_POST(self):
        url = urllib.parse.urlparse(self.path)
        if url.path != '/result':
            self.send_error(404)
            return
        body = self.rfile.read(int(self.headers.get('Content-Length', 0)))
        with open(output, 'wb') as f:
            f.write(body)
        failures = urllib.parse.parse_qs(url.query).get('failures', ['1'])[0]
        result['code'] = 0 if failures == '0' else 1
        self.send_response(200)
        self.send_header('Content-Length', '0')
        self.end_headers()
        threading.Thread(target=server.shutdown).start()

    def log_message(self, format, *args):
        sys.stderr.write('[serve] ' + (format % args) + '\n')


server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), functools.partial(Handler, directory=build))
with open(port_file, 'w') as f:
    f.write(str(server.server_address[1]))
threading.Timer(timeout, server.shutdown).start()
server.serve_forever()
sys.exit(result['code'])
