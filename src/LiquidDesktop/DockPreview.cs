using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
internal sealed class DockPreview:Form {
 [StructLayout(LayoutKind.Sequential)]struct RECT{public int l,t,r,b;public RECT(int x,int y,int w,int h){l=x;t=y;r=x+w;b=y+h;}}
 [StructLayout(LayoutKind.Sequential)]struct PROPS{public uint flags;public RECT dest,source;public byte opacity;[MarshalAs(UnmanagedType.Bool)]public bool visible;[MarshalAs(UnmanagedType.Bool)]public bool client;}
 [StructLayout(LayoutKind.Sequential)]struct SIZE{public int x,y;}
 [DllImport("dwmapi.dll")]static extern int DwmRegisterThumbnail(IntPtr dest,IntPtr source,out IntPtr thumb);
 [DllImport("dwmapi.dll")]static extern int DwmUpdateThumbnailProperties(IntPtr thumb,ref PROPS props);
 [DllImport("dwmapi.dll")]static extern int DwmUnregisterThumbnail(IntPtr thumb);
 [DllImport("dwmapi.dll")]static extern int DwmQueryThumbnailSourceSize(IntPtr thumb,out SIZE size);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetWindowLongPtr(IntPtr h,int index);
 [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]static extern IntPtr SetWindowLongPtr(IntPtr h,int index,IntPtr value);
 readonly DockEntry entry;readonly List<IntPtr> thumbs=new List<IntPtr>();readonly List<Rectangle> tiles=new List<Rectangle>();
 int page,selected,columns=4;Rectangle dock;Color ink;bool keyboardMode;
 public string EntryId {get{return entry.id;}}
 public bool KeyboardMode {get{return keyboardMode;}}
 protected override bool ShowWithoutActivation{get{return !keyboardMode;}}
 protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x80;if(!keyboardMode)p.ExStyle|=0x08000000;return p;}}
 public DockPreview(DockEntry item,Rectangle dockBounds,bool astro):this(item,dockBounds,astro,false){}
 public DockPreview(DockEntry item,Rectangle dockBounds,bool astro,bool keyboard){
  entry=item;dock=dockBounds;keyboardMode=keyboard;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;
  BackColor=astro?Color.FromArgb(24,36,60):Color.FromArgb(237,244,252);ink=astro?Color.FromArgb(231,239,252):Color.FromArgb(27,42,66);Font=new Font("Microsoft YaHei UI",9);KeyPreview=true;AccessibleName=item.name+" 窗口预览";
  Shown+=(s,e)=>{Build();if(keyboardMode){Activate();Focus();}};
  MouseClick+=(s,e)=>{for(int i=0;i<tiles.Count;i++)if(tiles[i].Contains(e.Location)){SelectWindow(page*columns+i);return;}if(e.Y<29){EnableKeyboard();return;}if(entry.windows.Count>columns&&e.Y>Height-32)ChangePage(e.X<Width/2?-1:1);};
  MouseWheel+=(s,e)=>ChangePage(e.Delta<0?1:-1);
  Deactivate+=(s,e)=>{if(keyboardMode)Close();};
 }
 public void EnableKeyboard(){if(!keyboardMode){keyboardMode=true;SetWindowLongPtr(Handle,-20,new IntPtr(GetWindowLongPtr(Handle,-20).ToInt64()&~0x08000000L));}Activate();Focus();Invalidate();}
 void SelectWindow(int index){if(index<0||index>=entry.windows.Count)return;var window=entry.windows[index];Close();DockBackend.Activate(window);}
 internal static int NextSelection(int current,int delta,int count){return count==0?0:(current+delta%count+count)%count;}
 internal static int ColumnsForWidth(int width){return Math.Max(1,Math.Min(4,(width-36)/180));}
 void ChangePage(int direction){int pages=(entry.windows.Count+columns-1)/columns;if(pages<=1)return;page=(page+direction+pages)%pages;selected=page*columns;Build();}
 protected override bool IsInputKey(Keys key){var code=key&Keys.KeyCode;return code==Keys.Left||code==Keys.Right||code==Keys.Up||code==Keys.Down||code==Keys.Home||code==Keys.End||base.IsInputKey(key);}
 protected override bool ProcessCmdKey(ref Message message,Keys key){var code=key&Keys.KeyCode;if(keyboardMode&&(code==Keys.Left||code==Keys.Right||code==Keys.Up||code==Keys.Down||code==Keys.Home||code==Keys.End||code==Keys.PageUp||code==Keys.PageDown||code==Keys.Tab||code==Keys.Enter||code==Keys.Escape)){OnKeyDown(new KeyEventArgs(key));return true;}return base.ProcessCmdKey(ref message,key);}
 protected override void OnKeyDown(KeyEventArgs e){
  if(e.KeyCode==Keys.Escape){e.Handled=true;e.SuppressKeyPress=true;Close();return;}
  if(e.KeyCode==Keys.Enter){e.Handled=true;e.SuppressKeyPress=true;SelectWindow(selected);return;}
  int next=selected;
  if(e.KeyCode==Keys.Right||e.KeyCode==Keys.Down||e.KeyCode==Keys.Tab&&!e.Shift)next=NextSelection(selected,1,entry.windows.Count);
  else if(e.KeyCode==Keys.Left||e.KeyCode==Keys.Up||e.KeyCode==Keys.Tab&&e.Shift)next=NextSelection(selected,-1,entry.windows.Count);
  else if(e.KeyCode==Keys.Home)next=0;else if(e.KeyCode==Keys.End)next=Math.Max(0,entry.windows.Count-1);
  else if(e.KeyCode==Keys.PageDown){ChangePage(1);e.Handled=true;return;}else if(e.KeyCode==Keys.PageUp){ChangePage(-1);e.Handled=true;return;}else{base.OnKeyDown(e);return;}
  selected=next;e.Handled=true;e.SuppressKeyPress=true;int target=selected/columns;if(target!=page){page=target;Build();}else Invalidate();
 }
 void Clear(){foreach(var h in thumbs)DwmUnregisterThumbnail(h);thumbs.Clear();tiles.Clear();}
 void Build(){
  Clear();var area=Screen.FromRectangle(dock).WorkingArea;columns=ColumnsForWidth(area.Width);page=Math.Min(page,Math.Max(0,(entry.windows.Count-1)/columns));int count=Math.Min(columns,entry.windows.Count-page*columns),tileWidth=Math.Min(236,Math.Max(100,(area.Width-36)/Math.Max(1,count)));
  ClientSize=new Size(count==0?200:count*tileWidth+20,count==0?64:entry.windows.Count>columns?214:186);
  Location=new Point(Math.Max(area.Left+8,Math.Min(area.Right-Width-8,Cursor.Position.X-Width/2)),Math.Max(area.Top+8,Math.Min(area.Bottom-Height-8,dock.Top-Height-14)));
  using(var path=new GraphicsPath()){int d=24;path.AddArc(0,0,d,d,180,90);path.AddArc(Width-d,0,d,d,270,90);path.AddArc(Width-d,Height-d,d,d,0,90);path.AddArc(0,Height-d,d,d,90,90);path.CloseFigure();var old=Region;Region=new Region(path);if(old!=null)old.Dispose();}
  for(int i=0;i<count;i++){
   var tile=new Rectangle(10+i*tileWidth,30,tileWidth-10,141);tiles.Add(tile);IntPtr h;if(DwmRegisterThumbnail(Handle,new IntPtr(entry.windows[page*columns+i].handle),out h)!=0)continue;thumbs.Add(h);SIZE size;if(DwmQueryThumbnailSourceSize(h,out size)!=0)continue;
   float scale=Math.Min((tile.Width-8)/(float)Math.Max(1,size.x),107f/Math.Max(1,size.y));int w=(int)(size.x*scale),ht=(int)(size.y*scale);var props=new PROPS{flags=1|4|8|16,dest=new RECT(tile.X+(tile.Width-w)/2,tile.Y+(111-ht)/2,w,ht),opacity=255,visible=true,client=false};DwmUpdateThumbnailProperties(h,ref props);
  }
  Invalidate();
 }
 protected override void OnPaint(PaintEventArgs e){
  base.OnPaint(e);TextRenderer.DrawText(e.Graphics,entry.name+" · "+entry.windows.Count+" 个窗口"+(keyboardMode?"  方向键选择 · Enter 打开 · Esc 关闭":"  点击标题使用键盘"),Font,new Rectangle(12,7,Width-24,22),ink,TextFormatFlags.EndEllipsis);
  for(int i=0;i<tiles.Count;i++){var tile=tiles[i];TextRenderer.DrawText(e.Graphics,entry.windows[page*columns+i].title,Font,new Rectangle(tile.X+4,145,tile.Width-8,23),ink,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);if(keyboardMode&&page*columns+i==selected)using(var pen=new Pen(Color.FromArgb(87,159,243),2))e.Graphics.DrawRectangle(pen,tile.X+1,tile.Y+1,tile.Width-2,tile.Height-2);}
  if(entry.windows.Count>columns)TextRenderer.DrawText(e.Graphics,"‹ 上一组                  "+(page+1)+" / "+((entry.windows.Count+columns-1)/columns)+"                  下一组 ›",Font,new Rectangle(8,Height-28,Width-16,22),ink,TextFormatFlags.HorizontalCenter);
 }
 protected override void Dispose(bool disposing){Clear();base.Dispose(disposing);}
}
