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
 readonly DockEntry entry;readonly List<IntPtr> thumbs=new List<IntPtr>();readonly List<Rectangle> tiles=new List<Rectangle>();int page;Rectangle dock;Color ink;
 public string EntryId {get{return entry.id;}}
 protected override bool ShowWithoutActivation{get{return true;}}
 protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x08000080;return p;}}
 public DockPreview(DockEntry item,Rectangle dockBounds,bool astro){entry=item;dock=dockBounds;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;BackColor=astro?Color.FromArgb(24,36,60):Color.FromArgb(237,244,252);ink=astro?Color.FromArgb(231,239,252):Color.FromArgb(27,42,66);Font=new Font("Microsoft YaHei UI",9);Shown+=(s,e)=>Build();MouseClick+=(s,e)=>{for(int i=0;i<tiles.Count;i++)if(tiles[i].Contains(e.Location)){var w=entry.windows[page*4+i];Close();DockBackend.Activate(w);return;}if(entry.windows.Count>4&&e.Y>Height-32){page=(page+(e.X<Width/2?-1:1)+(entry.windows.Count+3)/4)%((entry.windows.Count+3)/4);Build();}};MouseWheel+=(s,e)=>{if(entry.windows.Count>4){page=(page+(e.Delta<0?1:-1)+(entry.windows.Count+3)/4)%((entry.windows.Count+3)/4);Build();}};}
 void Clear(){foreach(var h in thumbs)DwmUnregisterThumbnail(h);thumbs.Clear();tiles.Clear();}
 void Build(){Clear();int count=Math.Min(4,entry.windows.Count-page*4);ClientSize=new Size(count==0?200:count*236+20,count==0?42:entry.windows.Count>4?206:182);var area=Screen.FromRectangle(dock).Bounds;Location=new Point(Math.Max(area.Left+8,Math.Min(area.Right-Width-8,Cursor.Position.X-Width/2)),dock.Top-Height-14);using(var path=new GraphicsPath()){int d=24;path.AddArc(0,0,d,d,180,90);path.AddArc(Width-d,0,d,d,270,90);path.AddArc(Width-d,Height-d,d,d,0,90);path.AddArc(0,Height-d,d,d,90,90);path.CloseFigure();var old=Region;Region=new Region(path);if(old!=null)old.Dispose();}
  for(int i=0;i<count;i++){var tile=new Rectangle(10+i*236,29,226,141);tiles.Add(tile);IntPtr h;if(DwmRegisterThumbnail(Handle,new IntPtr(entry.windows[page*4+i].handle),out h)!=0)continue;thumbs.Add(h);SIZE size;DwmQueryThumbnailSourceSize(h,out size);float scale=Math.Min(226f/Math.Max(1,size.x),111f/Math.Max(1,size.y));int w=(int)(size.x*scale),ht=(int)(size.y*scale);var props=new PROPS{flags=1|4|8|16,dest=new RECT(tile.X+(226-w)/2,tile.Y+(111-ht)/2,w,ht),opacity=255,visible=true,client=false};DwmUpdateThumbnailProperties(h,ref props);}
  Invalidate();}
 protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);TextRenderer.DrawText(e.Graphics,entry.name+(entry.windows.Count>0?" · "+entry.windows.Count+" 个窗口":""),Font,new Rectangle(12,8,Width-24,24),ink,TextFormatFlags.EndEllipsis);for(int i=0;i<tiles.Count;i++)TextRenderer.DrawText(e.Graphics,entry.windows[page*4+i].title,Font,new Rectangle(tiles[i].X,145,226,23),ink,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);if(entry.windows.Count>4)TextRenderer.DrawText(e.Graphics,"‹ 上一组                  "+(page+1)+" / "+((entry.windows.Count+3)/4)+"                  下一组 ›",Font,new Rectangle(8,Height-28,Width-16,22),ink,TextFormatFlags.HorizontalCenter);}
 protected override void Dispose(bool disposing){Clear();base.Dispose(disposing);}
}
