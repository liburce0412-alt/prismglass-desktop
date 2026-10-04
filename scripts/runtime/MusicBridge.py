"""Read NetEase's current window title and local queue; publish metadata for Rainmeter."""
import ctypes as C
from ctypes import wintypes as W
import json, time, urllib.request, os, subprocess
from pathlib import Path

ROOT=Path(__file__).resolve().parent
DATA=ROOT/'music-data';DATA.mkdir(exist_ok=True)
QUEUE=Path(os.environ['LOCALAPPDATA'])/'Netease/CloudMusic/webdata/file/playingList'
u=C.WinDLL('user32',use_last_error=True);k=C.WinDLL('kernel32',use_last_error=True)
u.GetWindowTextLengthW.argtypes=[W.HWND];u.GetWindowTextW.argtypes=[W.HWND,W.LPWSTR,C.c_int]
u.GetWindowThreadProcessId.argtypes=[W.HWND,C.POINTER(W.DWORD)]
k.OpenProcess.argtypes=[W.DWORD,W.BOOL,W.DWORD];k.OpenProcess.restype=W.HANDLE
k.QueryFullProcessImageNameW.argtypes=[W.HANDLE,W.DWORD,W.LPWSTR,C.POINTER(W.DWORD)]
k.CloseHandle.argtypes=[W.HANDLE]
k.CreateMutexW.argtypes=[C.c_void_p,W.BOOL,W.LPCWSTR];k.CreateMutexW.restype=W.HANDLE
mutex=k.CreateMutexW(None,False,'PrismGlass.MusicBridge')
if C.get_last_error()==183:raise SystemExit
CALLBACK=C.WINFUNCTYPE(W.BOOL,W.HWND,W.LPARAM)
u.EnumWindows.argtypes=[CALLBACK,W.LPARAM]
def current_title():
 found=[]
 @CALLBACK
 def visit(hwnd,param):
  n=u.GetWindowTextLengthW(hwnd)
  if not n:return True
  buf=C.create_unicode_buffer(n+1);u.GetWindowTextW(hwnd,buf,n+1)
  if ' - ' not in buf.value:return True
  pid=W.DWORD();u.GetWindowThreadProcessId(hwnd,C.byref(pid));h=k.OpenProcess(0x1000,False,pid.value)
  if h:
   path=C.create_unicode_buffer(32768);size=W.DWORD(32768)
   try:
    if k.QueryFullProcessImageNameW(h,0,path,C.byref(size)) and path.value.lower().endswith('\\cloudmusic.exe'):found.append(buf.value)
   finally:k.CloseHandle(h)
  return True
 u.EnumWindows(visit,0)
 return found[0] if found else ''
def metadata(title):
 song,artist=title.rsplit(' - ',1);cover='';album=''
 try:
  queue=json.loads(QUEUE.read_text('utf-8'))['list']
  for item in queue:
   track=item.get('track',{})
   if track.get('name')!=song:continue
   names='/'.join(a.get('name','') for a in track.get('artists',[]))
   if names!=artist:continue
   a=track.get('album',{});album=a.get('name','');url=a.get('picUrl') or a.get('cover','')
   if url.startswith('https://') and '.music.126.net/' in url:
    path=DATA/(str(track['id'])+'.jpg')
    if not path.exists():
     with urllib.request.urlopen(url,timeout=6) as response:payload=response.read(8*1024*1024)
     path.write_bytes(payload)
    cover=str(path)
   break
 except Exception:pass
 return song,artist,cover,album
helper=ROOT/'LiquidDesktop'/'MediaSession.ps1'
if helper.exists():
 subprocess.Popen([str(Path(os.environ['WINDIR'])/'System32/WindowsPowerShell/v1.0/powershell.exe'),'-NoProfile','-ExecutionPolicy','Bypass','-File',str(helper),'-OwnerPid',str(os.getpid()),'-DataRoot',str(DATA)],creationflags=subprocess.CREATE_NO_WINDOW,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
last=None
while True:
 try:
  title=current_title()
  if title!=last:
   fields=metadata(title) if title else ('网易云音乐','打开网易云并播放歌曲','','')
   value='\n'.join(str(x).replace('\n',' ').replace('\r',' ') for x in fields)+'\n'
   temp=DATA/'now.tmp';temp.write_text(value,encoding='utf-8');temp.replace(DATA/'now.txt')
   last=title
 except Exception:pass
 time.sleep(1.5)
