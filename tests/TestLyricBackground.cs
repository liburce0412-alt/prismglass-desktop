using System;using System.IO;using System.Drawing;using System.Linq;using System.Threading;
internal static class TestLyricBackground {
 static int checks;
 static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 static void Picture(string path,Color left,Color right){using(var bitmap=new Bitmap(60,30))using(var g=Graphics.FromImage(bitmap)){g.Clear(left);using(var brush=new SolidBrush(right))g.FillRectangle(brush,30,0,30,30);bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);}}
 static LyricPaletteState Ready(string root,MediaSnapshot song){LyricPaletteState result=null;for(int i=0;i<100;i++){result=LyricBackgroundPalette.Read(root,song);if(result.status=="使用当前专辑的配色")return result;Thread.Sleep(20);}throw new Exception("Palette did not finish");}
 [STAThread]static int Main(){string fixture=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"lyric-background-"+Guid.NewGuid().ToString("N")),root=Path.Combine(fixture,"app"),data=Path.Combine(fixture,"music-data");Directory.CreateDirectory(root);Directory.CreateDirectory(data);
  try{
   string cover=Path.Combine(data,"cover.png"),next=Path.Combine(data,"session-cover-1234567890abcdef.bin");Picture(Path.Combine(root,"wallpaper.png"),Color.DarkBlue,Color.SteelBlue);Picture(cover,Color.Red,Color.Green);Picture(next,Color.Gold,Color.Purple);
   var colors=LyricBackgroundPalette.Extract(cover);Check(colors.Length==3&&colors.Distinct().Count()>=2,"thumbnail retains distinct cover colors");
   var song=new MediaSnapshot{title="当前歌曲",artist="当前歌手"};File.WriteAllLines(Path.Combine(data,"now.txt"),new[]{song.title,song.artist,cover});Check(LyricBackgroundPalette.CoverPath(root,song)==cover,"local cover requires matching current track metadata");
   var palette=Ready(root,song);Check(palette.album.SequenceEqual(colors),"background worker returns current album colors");
   var different=new MediaSnapshot{title="其他歌曲",artist="当前歌手"};Check(LyricBackgroundPalette.CoverPath(root,different)=="","stale cover is rejected for a new track");Check(LyricBackgroundPalette.Read(root,different).status.Contains("跟随壁纸"),"missing album falls back to wallpaper with explicit status");
   File.WriteAllLines(Path.Combine(data,"now.txt"),new[]{song.title,song.artist,Path.Combine(root,"wallpaper.png")});Check(LyricBackgroundPalette.CoverPath(root,song)=="","external cover paths are not accepted");
   different.cover=Path.GetFileName(next);Check(LyricBackgroundPalette.CoverPath(root,different)==next,"system media thumbnail is associated by its generated cache ID");
   var second=Ready(root,different);Check(second.album.SequenceEqual(LyricBackgroundPalette.Extract(next))&&!second.album.SequenceEqual(palette.album),"track switch never returns the previous album palette");
   different.cover="../app/wallpaper.png";Check(LyricBackgroundPalette.CoverPath(root,different)=="","thumbnail path traversal is rejected");
   File.WriteAllLines(Path.Combine(data,"now.txt"),new[]{"wrong title",song.artist,cover});song.cover="session-cover-0000000000000000.bin";File.WriteAllText(Path.Combine(data,song.cover),"not an image");for(int i=0;i<5;i++){LyricBackgroundPalette.Read(root,song);Thread.Sleep(20);}Check(LyricBackgroundPalette.Read(root,song).status.Contains("跟随壁纸"),"invalid image cannot break lyric playback");
   var appearance=new LyricsAppearance{backgroundTheme="invalid",backgroundOpacity=101,backgroundMotion=true}.Normalize();Check(appearance.backgroundTheme=="transparent"&&appearance.backgroundOpacity==100&&appearance.backgroundMotion,"invalid theme and opacity are normalized without changing independent motion choice");
   var settings=new LyricsSettings{enabled=false,online=false,appearance=new LyricsAppearance{backgroundTheme="album",backgroundOpacity=61,backgroundMotion=true}};settings.Save(root);var restored=LyricsSettings.Load(root);Check(!restored.enabled&&!restored.online&&restored.appearance.backgroundTheme=="album"&&restored.appearance.backgroundOpacity==61&&restored.appearance.backgroundMotion,"background persists without enabling lyrics or network access");
   Console.WriteLine("Completed "+checks+" lyric background checks.");return 0;
  }catch(Exception ex){Console.WriteLine(ex);return 1;}finally{Directory.Delete(fixture,true);}
 }
}
