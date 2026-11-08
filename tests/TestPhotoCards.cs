using System;using System.Drawing;using System.Drawing.Imaging;using System.IO;using System.Linq;
internal static class TestPhotoCards {
 static int checks;static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);checks++;}
 static void Reject(Action action,string name){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}catch(IOException){rejected=true;}Check(rejected,name);}
 static void Picture(string path,int width,int height){using(var image=new Bitmap(width,height))using(var graphics=Graphics.FromImage(image)){graphics.Clear(Color.CornflowerBlue);using(var brush=new SolidBrush(Color.Gold))graphics.FillEllipse(brush,10,10,width/3,height/3);image.Save(path,ImageFormat.Png);}}
 static int Main(){string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"photos-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);try{
  var store=new PhotoCards(root);Check(store.ReadAll().Count==0,"a fresh profile does not import or enable photos");
  string landscape=Path.Combine(root,"Landscape.png"),portrait=Path.Combine(root,"Portrait.png"),square=Path.Combine(root,"Square.png");Picture(landscape,1200,800);Picture(portrait,600,900);Picture(square,800,800);
  var first=store.Import("Photo",landscape);var second=store.Import("Photo2",portrait);var third=store.Import("Photo3",square);
  Check(first.width==290&&first.height==197&&second.width==197&&second.height==290&&third.width==290&&third.height==290,"independent landscape portrait and square cards preserve source proportions");
  Check(store.ReadAll().Select(p=>p.id).SequenceEqual(new[]{"Photo","Photo2","Photo3"}),"multiple photos remain independent persistent cards");
  Check(!File.ReadAllText(Path.Combine(root,"photo-cards","Photo.txt")).Contains(root)&&first.image.StartsWith("photo-cards/"),"metadata retains no original path and uses same-origin image resources");
  string old=File.ReadAllText(Path.Combine(root,"photo-cards","Photo.txt")),oldToken=first.token;string invalid=Path.Combine(root,"Invalid.png");File.WriteAllText(invalid,"not a picture");
  Reject(()=>store.Import("Photo",invalid),"invalid replacement is rejected");Check(File.ReadAllText(Path.Combine(root,"photo-cards","Photo.txt"))==old&&File.Exists(Path.Combine(root,"photo-cards",oldToken+".png")),"failed replacement preserves the old photo and metadata");
  first=store.Import("Photo",portrait);Check(first.token!=oldToken&&!File.Exists(Path.Combine(root,"photo-cards",oldToken+".png"))&&File.Exists(landscape),"replacement updates only the selected card and removes only its old copy");
  store.Remove("Photo2");Check(store.Read("Photo2")==null&&File.Exists(portrait)&&store.Read("Photo3")!=null,"removing one card preserves originals and other photo cards");
  Reject(()=>store.Import("../Photo",square),"slot traversal is rejected");Reject(()=>PhotoCards.CardSize(16000,16000),"oversized decoder dimensions are rejected");Reject(()=>PhotoCards.CardSize(10000,100),"extreme source ratios are rejected before allocation");
  byte[] bomb=new byte[33];Array.Copy(new byte[]{137,80,78,71,13,10,26,10},bomb,8);bomb[16]=127;bomb[20]=127;File.WriteAllBytes(invalid,bomb);Reject(()=>store.Import("Photo",invalid),"a compressed PNG claiming huge dimensions is rejected before decoding");
  foreach(string id in PhotoCards.Slots)if(store.Read(id)==null)store.Import(id,square);Reject(()=>store.FirstEmpty(),"the library stops at six independent photos");
  var settings=new DesktopPreferences(root);settings.Apply(new System.Collections.Generic.Dictionary<string,object>{{"desktopWidgets",PhotoCards.Slots},{"autoHideWidgets",new[]{"Photo2","Photo6"}}},settings.Revision);var reload=new DesktopPreferences(root);Check(reload.Current.desktopWidgets.Count==6&&reload.Current.autoHideWidgets.SequenceEqual(new[]{"Photo2","Photo6"}),"all six photo slots persist independent visibility and occlusion choices");
  Check(PhotoCards.CardSize(1,4)==new Size(80,290),"the supported narrowest ratio remains inside bounded native dimensions");
  string oriented=Path.Combine(root,"Rotated.jpg");using(var bitmap=new Bitmap(900,600))bitmap.Save(oriented,ImageFormat.Jpeg);byte[] jpeg=File.ReadAllBytes(oriented);
  byte[] exif={255,225,0,34,69,120,105,102,0,0,73,73,42,0,8,0,0,0,1,0,18,1,3,0,1,0,0,0,6,0,0,0,0,0,0,0};
  using(var output=File.Create(oriented)){output.Write(jpeg,0,2);output.Write(exif,0,exif.Length);output.Write(jpeg,2,jpeg.Length-2);}
  var rotated=store.Import("Photo6",oriented);Check(rotated.width==197&&rotated.height==290,"EXIF orientation is applied before fitting the card proportions");
  Console.WriteLine("Completed "+checks+" photo-store checks.");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{if(Directory.Exists(root))Directory.Delete(root,true);}}
}
