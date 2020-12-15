import cgi
import mimetypes
import posixpath
import shutil
import urllib.parse
import os
import re
from http.server import BaseHTTPRequestHandler, HTTPServer

host = '127.0.0.1' 
port = 8988 

class SimpleHTTPRequestHandler(BaseHTTPRequestHandler):
    if not mimetypes.inited:
        # try to read system mime.types
        mimetypes.init()  
    extensions_map = mimetypes.types_map.copy()
    extensions_map.update({
        '': 'application/octet-stream',  # Default
        '.py': 'text/plain',
        '.c': 'text/plain',
        '.h': 'text/plain',
    })
    
    def do_GET(self):
        f = self.send_head()
        if f:
            self.copyfile(f, self.wfile)
            f.close()
            
    def do_HEAD(self):
        f = self.send_head()
        if f:
            f.close()
            
    def send_head(self):
        path = self.translate_path(self.path)
        f = None
        if os.path.isdir(path):
            if not self.path.endswith('/'):
                self.send_response(301)
                self.send_header("Location", self.path + "/")
                self.end_headers()
                return None
            for index in "index.html", "index.htm":
                index = os.path.join(path, index)
                if os.path.exists(index):
                    path = index
                    break

        ctype = self.guess_type(path)
        offset = 0
        try:
            f = open(path, 'rb')
            # 支持断点续传
            content_ranges = self.headers['Range']
            if None != content_ranges:
                # Range: bytes=n-m
                match = re.match('bytes=(\d+)-(\d*)', content_ranges)
                if match:
                    offset = int(match.group(1))
                    f.seek(offset)
                    print('f.seek ' + str(offset))
        except IOError:
            self.send_error(404, "File not found")
            return None
        self.send_response(200)
        self.send_header("Content-type", ctype)
        # 返回文件描述符fd的状态
        fs = os.fstat(f.fileno())        
        realsize = fs.st_size
        self.send_header("Content-Length", str(realsize - offset))
        self.send_header("Last-Modified", self.date_time_string(fs.st_mtime))
        self.end_headers()
        return f

    def guess_type(self, path):
        base, ext = posixpath.splitext(path)
        if ext in self.extensions_map:
            return self.extensions_map[ext]
        ext = ext.lower()
        if ext in self.extensions_map:
            return self.extensions_map[ext]
        else:
            return self.extensions_map['']

    def translate_path(self, path):
        path = path.split('?', 1)[0]
        path = path.split('#', 1)[0]
        path = posixpath.normpath(urllib.parse.unquote(path))
        words = path.split('/')
        words = [_f for _f in words if _f]
        path = os.getcwd()
        for word in words:
            drive, word = os.path.splitdrive(word)
            head, word = os.path.split(word)
            if word in (os.curdir, os.pardir):
                continue
            path = os.path.join(path, word)
        return path
    
    def copyfile(self, source, outputfile):
        shutil.copyfileobj(source, outputfile)
    
    def do_POST(self):
        form = cgi.FieldStorage(
            fp=self.rfile,
            headers=self.headers,
            environ={'REQUEST_METHOD':'POST',
                     'CONTENT_TYPE':self.headers['Content-Type'],
                     }
        )
        self.send_response(200)
        self.end_headers()
        desc = form['desc'].value
        # 文件名
        filename = form['file_data'].filename
        # 文件的字节流
        filevalue = form['file_data'].value
        filesize = len(filevalue)
        # 文件写入
        with open(filename, 'wb') as f:
                f.write(filevalue)
        msg = 'upload success, file: %s, size:  %d, desc: %s'%(filename, filesize, desc)
        print(msg)
        # 返回消息给客户端
        self.wfile.write(msg.encode("utf-8"))
        return

 
if '__main__' == __name__ :
    sever = HTTPServer((host, port), SimpleHTTPRequestHandler)
    print("我是web服务器，地址：http://localhost:" + str(port))
    sever.serve_forever()
