using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// Original rounded pointer drawing, inspired by the visible Computer Use
// pointer silhouette. This does not extract or redistribute Codex assets.
class CreatePrismCursor {
 static void Main(string[] args){
  int size=64;bool light=args.Length>2&&args[2]=="light";
  using(var image=new Bitmap(size,size,PixelFormat.Format32bppArgb)){
   using(var g=Graphics.FromImage(image))using(var shape=new GraphicsPath()){
    g.SmoothingMode=SmoothingMode.AntiAlias;g.ScaleTransform(2,2);
    shape.StartFigure();shape.AddBezier(6,3.5f,3.5f,2.3f,2.6f,3.8f,3.4f,6.1f);
    shape.AddLine(3.4f,6.1f,9.2f,25.5f);shape.AddBezier(9.2f,25.5f,9.8f,27.5f,11.9f,27.4f,12.7f,25.4f);
    shape.AddLine(12.7f,25.4f,15.4f,18.8f);shape.AddBezier(15.4f,18.8f,15.6f,18.2f,16,17.8f,16.7f,17.6f);
    shape.AddLine(16.7f,17.6f,24.1f,15);shape.AddBezier(24.1f,15,26.4f,14.2f,26.4f,12.1f,24.2f,11.1f);
    shape.AddLine(24.2f,11.1f,6,3.5f);shape.CloseFigure();
    using(var shadow=new Pen(Color.FromArgb(70,0,0,0),3.2f)){shadow.LineJoin=LineJoin.Round;g.TranslateTransform(.35f,.7f);g.DrawPath(shadow,shape);g.TranslateTransform(-.35f,-.7f);}
    using(var fill=new SolidBrush(light?Color.FromArgb(250,252,255):Color.FromArgb(24,27,34)))g.FillPath(fill,shape);
    using(var edge=new Pen(light?Color.FromArgb(43,52,67):Color.FromArgb(252,253,255),1.5f)){edge.LineJoin=LineJoin.Round;g.DrawPath(edge,shape);}
   }
   // Standard uncompressed CUR with alpha and a precise hotspot at the tip.
   int maskStride=((size+31)/32)*4,bytes=40+size*size*4+maskStride*size;
   using(var stream=File.Create(args[0]))using(var w=new BinaryWriter(stream)){
    w.Write((ushort)0);w.Write((ushort)2);w.Write((ushort)1);w.Write((byte)size);w.Write((byte)size);w.Write((byte)0);w.Write((byte)0);
    w.Write((ushort)7);w.Write((ushort)7);w.Write(bytes);w.Write(22);
    w.Write(40);w.Write(size);w.Write(size*2);w.Write((ushort)1);w.Write((ushort)32);w.Write(0);w.Write(size*size*4);w.Write(0);w.Write(0);w.Write(0);w.Write(0);
    for(int y=size-1;y>=0;y--)for(int x=0;x<size;x++){var c=image.GetPixel(x,y);w.Write(c.B);w.Write(c.G);w.Write(c.R);w.Write(c.A);}
    for(int y=size-1;y>=0;y--)for(int block=0;block<maskStride;block++){byte bits=0;for(int bit=0;bit<8;bit++){int x=block*8+bit;if(x<size&&image.GetPixel(x,y).A==0)bits|=(byte)(128>>bit);}w.Write(bits);}
   }
   if(args.Length>1)image.Save(args[1],ImageFormat.Png);
  }
 }
}
