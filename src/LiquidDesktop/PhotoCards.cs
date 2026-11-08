using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text.RegularExpressions;

internal sealed class PhotoCard {
 public string id,title,image,token;public int width,height;
}

// Only explicitly picked files enter this private, bounded image store. Rainmeter reads slot metadata.
internal sealed class PhotoCards {
 internal static readonly string[] Slots={"Photo","Photo2","Photo3","Photo4","Photo5","Photo6"};
 readonly string directory;
 internal PhotoCards(string root){directory=Path.Combine(root,"photo-cards");}
 static void Slot(string id){if(Array.IndexOf(Slots,id)<0)throw new ArgumentException("照片卡片无效。");}
 void CheckDirectory(){if(Directory.Exists(directory)&&(File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new IOException("照片存储目录不能是链接。");}
 internal PhotoCard Read(string id){
  Slot(id);CheckDirectory();string path=Path.Combine(directory,id+".txt");if(!File.Exists(path))return null;
  if(new FileInfo(path).Length>2048)throw new IOException("照片卡片记录无效。");var lines=File.ReadAllLines(path);int width,height;
  if(lines.Length<4||!Regex.IsMatch(lines[0],"^[a-f0-9]{32}$")||!Int32.TryParse(lines[1],out width)||!Int32.TryParse(lines[2],out height)||width<80||width>330||height<80||height>330)throw new IOException("照片卡片记录无效，原文件已保留。");
  return new PhotoCard{id=id,token=lines[0],width=width,height=height,title=lines[3],image="photo-cards/"+lines[0]+".png"};
 }
 internal List<PhotoCard> ReadAll(){var result=new List<PhotoCard>();foreach(var id in Slots){var card=Read(id);if(card!=null)result.Add(card);}return result;}
 internal string FirstEmpty(){foreach(var id in Slots)if(Read(id)==null)return id;throw new IOException("最多可添加 6 张独立照片，请先移除一张。");}
 internal static Size CardSize(int width,int height){
  if(width<1||height<1||Math.Max(width,height)>12000||(long)width*height>24000000||(double)Math.Max(width,height)/Math.Min(width,height)>4)throw new IOException("请选择不超过 2400 万像素、长宽比在 1:4 至 4:1 之间的照片。");
  double scale=280d/Math.Max(width,height);return new Size((int)Math.Round(width*scale)+10,(int)Math.Round(height*scale)+10);
 }
 static int ReadBig(Stream input,int bytes){int value=0;for(int i=0;i<bytes;i++){int next=input.ReadByte();if(next<0)throw new IOException("照片文件不完整。");value=(value<<8)|next;}return value;}
 // Read dimensions before decoding, so a tiny compressed file cannot request an enormous bitmap.
 static Size Probe(Stream input){
  input.Position=0;int first=input.ReadByte(),second=input.ReadByte();
  if(first==137&&second==80){byte[] signature=new byte[6];if(input.Read(signature,0,6)!=6||signature[0]!=78||signature[1]!=71||signature[2]!=13||signature[3]!=10||signature[4]!=26||signature[5]!=10)throw new IOException("PNG 文件无效。");input.Position=16;int w=ReadBig(input,4),h=ReadBig(input,4);CardSize(w,h);input.Position=0;return new Size(w,h);}
  if(first==255&&second==216){
   while(input.Position<input.Length&&input.Position<1048576){
    int start=input.ReadByte();if(start!=255)throw new IOException("JPEG 文件无效。");int marker;do{marker=input.ReadByte();}while(marker==255);
    if(marker<0||marker==217||marker==218)break;if(marker==1||(marker>=208&&marker<=215))continue;
    int length=ReadBig(input,2);if(length<2||input.Position+length-2>input.Length)throw new IOException("JPEG 文件不完整。");
    if(marker>=192&&marker<=207&&marker!=196&&marker!=200&&marker!=204){input.ReadByte();int h=ReadBig(input,2),w=ReadBig(input,2);CardSize(w,h);input.Position=0;return new Size(w,h);}
    input.Position+=length-2;
   }
  }
  throw new IOException("请选择有效的 JPG 或 PNG 照片。");
 }
 static void Orient(Image image){
  if(Array.IndexOf(image.PropertyIdList,0x112)<0)return;var bytes=image.GetPropertyItem(0x112).Value;if(bytes.Length<2)return;
  switch(BitConverter.ToUInt16(bytes,0)){
   case 2:image.RotateFlip(RotateFlipType.RotateNoneFlipX);break;case 3:image.RotateFlip(RotateFlipType.Rotate180FlipNone);break;case 4:image.RotateFlip(RotateFlipType.Rotate180FlipX);break;
   case 5:image.RotateFlip(RotateFlipType.Rotate90FlipX);break;case 6:image.RotateFlip(RotateFlipType.Rotate90FlipNone);break;case 7:image.RotateFlip(RotateFlipType.Rotate270FlipX);break;case 8:image.RotateFlip(RotateFlipType.Rotate270FlipNone);break;
  }
 }
 internal PhotoCard Import(string id,string source){
  Slot(id);if(String.IsNullOrWhiteSpace(source)||!Path.IsPathRooted(source)||source.StartsWith("\\\\",StringComparison.Ordinal))throw new IOException("请选择本机的 JPG 或 PNG 照片。");
  string extension=Path.GetExtension(source);if(!extension.Equals(".png",StringComparison.OrdinalIgnoreCase)&&!extension.Equals(".jpg",StringComparison.OrdinalIgnoreCase)&&!extension.Equals(".jpeg",StringComparison.OrdinalIgnoreCase))throw new IOException("请选择 JPG 或 PNG 照片。");
  PhotoCard result=null;DesktopSettingsFiles.Locked(()=>{
   CheckDirectory();var previous=Read(id);Directory.CreateDirectory(directory);string token=Guid.NewGuid().ToString("N"),target=Path.Combine(directory,token+".png");bool committed=false;
   try{
    using(var stream=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read)){
     if(stream.Length>25*1024*1024)throw new IOException("单张照片不能超过 25 MB。");Probe(stream);
     using(var image=Image.FromStream(stream,false,false)){
      Orient(image);Size size=CardSize(image.Width,image.Height);double scale=Math.Min(1,1280d/Math.Max(image.Width,image.Height));
      using(var copy=new Bitmap(Math.Max(1,(int)Math.Round(image.Width*scale)),Math.Max(1,(int)Math.Round(image.Height*scale)),PixelFormat.Format32bppPArgb)){
       using(var graphics=Graphics.FromImage(copy)){graphics.CompositingMode=CompositingMode.SourceCopy;graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;graphics.DrawImage(image,new Rectangle(Point.Empty,copy.Size));}copy.Save(target,ImageFormat.Png);
      }
      string title=Path.GetFileNameWithoutExtension(source).Replace('\r',' ').Replace('\n',' ');if(title.Length>100)title=title.Substring(0,100);
      DesktopSettingsFiles.Write(Path.Combine(directory,id+".txt"),token+"\n"+size.Width+"\n"+size.Height+"\n"+title+"\n");committed=true;result=Read(id);
     }
    }
   }finally{if(!committed&&File.Exists(target))File.Delete(target);}
   if(previous!=null)DeleteImage(previous.token);
  });return result;
 }
 void DeleteImage(string token){try{File.Delete(Path.Combine(directory,token+".png"));}catch(IOException){}catch(UnauthorizedAccessException){}}
 internal void Remove(string id){Slot(id);DesktopSettingsFiles.Locked(()=>{CheckDirectory();var old=Read(id);if(old==null)return;File.Delete(Path.Combine(directory,id+".txt"));DeleteImage(old.token);});}
}
