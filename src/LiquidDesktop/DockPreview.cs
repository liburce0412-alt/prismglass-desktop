using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
internal sealed class DockPreview:Form {
 internal static Action<Control> ConfigureMaterial;
 internal static Func<bool> MotionAllowed;
 [StructLayout(LayoutKind.Sequential)]struct RECT{public int l,t,r,b;public RECT(int x,int y,int w,int h){l=x;t=y;r=x+w;b=y+h;}}
 [StructLayout(LayoutKind.Sequential)]struct PROPS{public uint flags;public RECT dest,source;public byte opacity;[MarshalAs(UnmanagedType.Bool)]public bool visible;[MarshalAs(UnmanagedType.Bool)]public bool client;}
 [StructLayout(LayoutKind.Sequential)]struct SIZE{public int x,y;}
 [DllImport("dwmapi.dll")]static extern int DwmRegisterThumbnail(IntPtr dest,IntPtr source,out IntPtr thumb);
 [DllImport("dwmapi.dll")]static extern int DwmUpdateThumbnailProperties(IntPtr thumb,ref PROPS props);
 [DllImport("dwmapi.dll")]static extern int DwmUnregisterThumbnail(IntPtr thumb);
 [DllImport("dwmapi.dll")]static extern int DwmQueryThumbnailSourceSize(IntPtr thumb,out SIZE size);
 [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
 [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out RECT value,int size);
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out RECT rect);
 [DllImport("user32.dll")]static extern bool IsIconic(IntPtr window);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetWindowLongPtr(IntPtr h,int index);
 [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]static extern IntPtr SetWindowLongPtr(IntPtr h,int index,IntPtr value);
 sealed class Thumbnail {internal IntPtr handle,source;internal Rectangle tile;internal RECT crop,dest;internal bool ready;}
 readonly DockEntry entry;readonly List<Thumbnail> thumbs=new List<Thumbnail>();readonly List<Rectangle> tiles=new List<Rectangle>();
 int page,selected,columns=4;Rectangle dock;Color ink;bool keyboardMode;
 readonly Timer motion=new Timer{Interval=8};double progress;bool dismissing;int hovered=-1;Point resting;Point? placement;DateTime lastFrame,sourceCheck;
 public string EntryId {get{return entry.id;}}
 public bool KeyboardMode {get{return keyboardMode;}}
 protected override bool ShowWithoutActivation{get{return !keyboardMode;}}
 protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x80;if(!keyboardMode)p.ExStyle|=0x08000000;return p;}}
 public DockPreview(DockEntry item,Rectangle dockBounds,bool astro):this(item,dockBounds,astro,false){}
 public DockPreview(DockEntry item,Rectangle dockBounds,bool astro,bool keyboard){
  entry=item;dock=dockBounds;keyboardMode=keyboard;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;
  BackColor=astro?Color.FromArgb(24,36,60):Color.FromArgb(237,244,252);ink=astro?Color.FromArgb(231,239,252):Color.FromArgb(27,42,66);Font=new Font("Microsoft YaHei UI",9);KeyPreview=true;AccessibleName=item.name+" 窗口预览";
  Shown+=(s,e)=>{Build();resting=Location;lastFrame=DateTime.UtcNow;progress=MotionAllowed!=null&&!MotionAllowed()?1:0;Animate();motion.Start();if(keyboardMode){Activate();Focus();}};
  motion.Tick+=(s,e)=>Animate();MouseMove+=(s,e)=>{int next=tiles.FindIndex(r=>r.Contains(e.Location));if(next!=hovered){hovered=next;Invalidate();}if(dismissing)Revive();};MouseLeave+=(s,e)=>{hovered=-1;Invalidate();};
  MouseClick+=(s,e)=>{for(int i=0;i<tiles.Count;i++)if(tiles[i].Contains(e.Location)){SelectWindow(page*columns+i);return;}if(e.Y<29){EnableKeyboard();return;}if(entry.windows.Count>columns&&e.Y>Height-32)ChangePage(e.X<Width/2?-1:1);};
  MouseWheel+=(s,e)=>ChangePage(e.Delta<0?1:-1);
  Deactivate+=(s,e)=>{if(keyboardMode)Close();};
  if(ConfigureMaterial!=null){BackColor=Color.FromArgb(24,36,60);ink=Color.FromArgb(237,245,255);ConfigureMaterial(this);}
 }
 public void Dismiss(){if(IsDisposed||dismissing)return;dismissing=true;lastFrame=DateTime.UtcNow;motion.Interval=8;motion.Start();if(MotionAllowed!=null&&!MotionAllowed())Animate();}
 public void Revive(){if(!dismissing)return;dismissing=false;lastFrame=DateTime.UtcNow;motion.Interval=8;motion.Start();}
 internal void PlaceAt(Point location){placement=location;if(IsHandleCreated)Build();}
 void Animate(){
  var now=DateTime.UtcNow;double dt=Math.Min(.05,Math.Max(0,(now-lastFrame).TotalSeconds));lastFrame=now;
  bool reduced=MotionAllowed!=null&&!MotionAllowed();progress=reduced?(dismissing?0:1):Math.Max(0,Math.Min(1,progress+(dismissing?-1:1)*dt/.16));
  double eased=1-Math.Pow(1-progress,3);Opacity=Math.Max(.01,eased);if(progress==1&&!dismissing)motion.Interval=250;
  Location=new Point(resting.X,resting.Y+(int)((1-eased)*9));
  if(dismissing&&progress==0){Close();return;}
  if((now-sourceCheck).TotalMilliseconds>=250){sourceCheck=now;int removed=entry.windows.RemoveAll(w=>!DockBackend.Valid(w));if(removed>0){if(entry.windows.Count==0){Close();return;}selected=Math.Min(selected,entry.windows.Count-1);Build();resting=Location;}else foreach(var thumbnail in thumbs)UpdateThumbnail(thumbnail);}
 }
 public void EnableKeyboard(){if(!keyboardMode){keyboardMode=true;SetWindowLongPtr(Handle,-20,new IntPtr(GetWindowLongPtr(Handle,-20).ToInt64()&~0x08000000L));}Activate();Focus();Invalidate();}
 void SelectWindow(int index){if(index<0||index>=entry.windows.Count)return;var window=entry.windows[index];keyboardMode=false;DockBackend.HoldActivation();Close();try{DockBackend.Activate(window);}catch(InvalidOperationException ex){MessageBox.Show(ex.Message,"无法切换窗口",MessageBoxButtons.OK,MessageBoxIcon.Information);}}
 protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);int border=-2;DwmSetWindowAttribute(Handle,34,ref border,4);}
 internal static int NextSelection(int current,int delta,int count){return count==0?0:(current+delta%count+count)%count;}
 internal static int ColumnsForWidth(int width){return Math.Max(1,Math.Min(4,(width-36)/180));}
 void ChangePage(int direction){int pages=(entry.windows.Count+columns-1)/columns;if(pages<=1)return;page=(page+direction+pages)%pages;selected=page*columns;Build();progress=.3;lastFrame=DateTime.UtcNow;motion.Interval=8;motion.Start();}
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
 void Clear(){foreach(var thumbnail in thumbs)DwmUnregisterThumbnail(thumbnail.handle);thumbs.Clear();tiles.Clear();}
 internal static Rectangle SourceCrop(IntPtr source,Size texture){
  var full=new Rectangle(Point.Empty,texture);RECT outer,visible;
  // Minimized HWND bounds describe the iconic title strip, not DWM's retained full-window texture.
  // Screen-frame coordinates are safe to crop only while they match that texture's pixel space.
  if(IsIconic(source)||!GetWindowRect(source,out outer)||Math.Abs((outer.r-outer.l)-texture.Width)>2||Math.Abs((outer.b-outer.t)-texture.Height)>2)return full;
  if(DwmGetWindowAttribute(source,9,out visible,16)!=0||visible.r<=visible.l||visible.b<=visible.t||visible.l<outer.l||visible.t<outer.t||visible.r>outer.r||visible.b>outer.b)return full;
  return Rectangle.Intersect(full,new Rectangle(visible.l-outer.l,visible.t-outer.t,visible.r-visible.l,visible.b-visible.t));
 }
 void UpdateThumbnail(Thumbnail thumbnail){
  SIZE size;if(DwmQueryThumbnailSourceSize(thumbnail.handle,out size)!=0||size.x<=0||size.y<=0)return;
  var crop=SourceCrop(thumbnail.source,new Size(size.x,size.y));if(crop.Width<=0||crop.Height<=0)return;
  var tile=thumbnail.tile;float scale=Math.Min((tile.Width-8)/(float)crop.Width,107f/crop.Height);int width=Math.Max(1,(int)(crop.Width*scale)),height=Math.Max(1,(int)(crop.Height*scale));
  var source=new RECT(crop.X,crop.Y,crop.Width,crop.Height);var dest=new RECT(tile.X+(tile.Width-width)/2,tile.Y+(111-height)/2,width,height);
  if(thumbnail.ready&&thumbnail.crop.Equals(source)&&thumbnail.dest.Equals(dest))return;
  var props=new PROPS{flags=1|2|4|8|16,source=source,dest=dest,opacity=255,visible=true,client=false};
  if(DwmUpdateThumbnailProperties(thumbnail.handle,ref props)==0){thumbnail.crop=source;thumbnail.dest=dest;thumbnail.ready=true;}
 }
 void Build(){
  Clear();var area=Screen.FromRectangle(dock).WorkingArea;columns=ColumnsForWidth(area.Width);page=Math.Min(page,Math.Max(0,(entry.windows.Count-1)/columns));int count=Math.Min(columns,entry.windows.Count-page*columns),tileWidth=Math.Min(236,Math.Max(100,(area.Width-36)/Math.Max(1,count)));
  ClientSize=new Size(count==0?200:count*tileWidth+20,count==0?64:entry.windows.Count>columns?214:186);
  var at=placement??new Point(Cursor.Position.X-Width/2,dock.Top-Height-14);Location=new Point(Math.Max(area.Left+8,Math.Min(area.Right-Width-8,at.X)),Math.Max(area.Top+8,Math.Min(area.Bottom-Height-8,at.Y)));resting=Location;
  using(var path=new GraphicsPath()){int d=36;path.AddArc(0,0,d,d,180,90);path.AddArc(Width-d,0,d,d,270,90);path.AddArc(Width-d,Height-d,d,d,0,90);path.AddArc(0,Height-d,d,d,90,90);path.CloseFigure();var old=Region;Region=new Region(path);if(old!=null)old.Dispose();}
  for(int i=0;i<count;i++){
   var tile=new Rectangle(10+i*tileWidth,30,tileWidth-10,141);tiles.Add(tile);var source=new IntPtr(entry.windows[page*columns+i].handle);IntPtr h;if(DwmRegisterThumbnail(Handle,source,out h)!=0)continue;
   var thumbnail=new Thumbnail{handle=h,source=source,tile=tile};thumbs.Add(thumbnail);UpdateThumbnail(thumbnail);
  }
  Invalidate();
 }
 protected override void OnPaint(PaintEventArgs e){
  base.OnPaint(e);TextRenderer.DrawText(e.Graphics,entry.name+" · "+entry.windows.Count+" 个窗口"+(keyboardMode?"  方向键选择 · Enter 打开 · Esc 关闭":"  点击标题使用键盘"),Font,new Rectangle(12,7,Width-24,22),ink,TextFormatFlags.EndEllipsis);
  for(int i=0;i<tiles.Count;i++){var tile=tiles[i];if(i==hovered)using(var brush=new SolidBrush(Color.FromArgb(26,174,220,255)))e.Graphics.FillRectangle(brush,tile);TextRenderer.DrawText(e.Graphics,entry.windows[page*columns+i].title,Font,new Rectangle(tile.X+4,145,tile.Width-8,23),ink,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);if(i==hovered||keyboardMode&&page*columns+i==selected)using(var pen=new Pen(Color.FromArgb(87,159,243),2))e.Graphics.DrawRectangle(pen,tile.X+1,tile.Y+1,tile.Width-2,tile.Height-2);}
  if(entry.windows.Count>columns)TextRenderer.DrawText(e.Graphics,"‹ 上一组                  "+(page+1)+" / "+((entry.windows.Count+columns-1)/columns)+"                  下一组 ›",Font,new Rectangle(8,Height-28,Width-16,22),ink,TextFormatFlags.HorizontalCenter);
 }
 protected override void Dispose(bool disposing){if(disposing){motion.Stop();motion.Dispose();}Clear();base.Dispose(disposing);}
}
