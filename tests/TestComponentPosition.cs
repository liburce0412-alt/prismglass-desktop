using System;using System.Drawing;using System.IO;using System.Windows.Forms;
internal static class TestComponentPosition {
 static int checks;static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 [STAThread]static int Main(string[] args){try{
  string root=args[0];var area=Screen.PrimaryScreen.Bounds;var visible=new Rectangle(4,4,88,200);var defaults=new Rectangle(area.Left+24,area.Top+80,852,680);var saved=Point.Empty;string errors="";
  Check(ComponentPosition.Clamp(new Point(-2400,-400),visible,new Rectangle(-1920,-200,1920,1080))==new Point(-1924,-204),"negative-origin display clamps the visible glass, not the transparent host");
  Check(ComponentPosition.Clamp(new Point(1900,1000),visible,new Rectangle(0,0,1920,1080))==new Point(1828,876),"right and lower screen edges retain the complete visible rail");
  using(var form=new Form{FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual}){
   var handle=form.Handle;
   using(var position=new ComponentPosition(root,"left",()=>form.Bounds,next=>form.Bounds=next,message=>errors=message)){
    position.Arrange(defaults,area,new Point(4,4),visible);Check(form.Bounds==defaults,"native window begins at its established default");
    position.Begin(new Point(100,100));position.Step(new Point(300,180),true,false);Check(position.Dragging&&form.Location==new Point(defaults.X+200,defaults.Y+80),"grip drag moves the native host in both axes");
    position.Step(new Point(300,180),false,false);saved=form.Location;Check(!position.Dragging&&File.Exists(Path.Combine(root,"component-positions.json")),"release persists the position once");
    string positionBytes=File.ReadAllText(Path.Combine(root,"component-positions.json"));ComponentPosition.LayoutLocked=true;
    position.Begin(new Point(100,100));position.Receive("position:right");position.Reset();Check(!position.Dragging&&form.Location==saved&&File.ReadAllText(Path.Combine(root,"component-positions.json"))==positionBytes,"locked layout blocks drag, keyboard nudge and reset without changing saved positions");
    ComponentPosition.DisplaysChanged();Check(position.DisplayChanged,"same-geometry display notifications invalidate each component's monitor anchor");position.Arrange(defaults,area,new Point(4,4),visible);Check(!position.DisplayChanged&&form.Location==saved&&File.ReadAllText(Path.Combine(root,"component-positions.json"))==positionBytes,"display identity refresh at the same geometry preserves locked anchors");
    ComponentPosition.LayoutLocked=false;position.Begin(new Point(100,100));position.Step(new Point(140,130),true,false);ComponentPosition.LayoutLocked=true;position.Step(new Point(180,150),true,false);Check(!position.Dragging&&form.Location==saved,"locking during a grip drag restores its saved origin");ComponentPosition.LayoutLocked=false;
    position.Begin(new Point(100,100));position.Step(new Point(150,150),true,false);position.Receive("position:cancel");Check(form.Location==saved,"Escape cancels movement without changing the saved anchor");
    position.Arrange(defaults,area,new Point(4,4),new Rectangle(4,4,area.Width-8,200));Check(form.Left==area.Left+4,"expanded glass temporarily fits inside the screen");
    position.Arrange(defaults,area,new Point(4,4),visible);Check(form.Location==saved,"closing the panel returns to the explicitly saved anchor");
   }
   using(var reloaded=new ComponentPosition(root,"left",()=>form.Bounds,next=>form.Bounds=next,message=>errors=message)){
    reloaded.Arrange(defaults,area,new Point(4,4),visible);Check(form.Location==saved,"recreated component restores its display-specific position");
    using(var other=new ComponentPosition(root,"right",()=>form.Bounds,next=>form.Bounds=next,message=>errors=message)){other.Arrange(defaults,area,new Point(4,4),visible);other.Receive("position:right");}
    reloaded.Reset();Check(form.Location==defaults.Location,"reset restores the original component location");
    Check(DesktopSettingsFiles.Map(File.ReadAllText(Path.Combine(root,"component-positions.json"))).Count==1,"reset preserves another component's position");
   }
  }
  Check(errors=="","native position operations report no errors");Console.WriteLine(checks+" checks passed");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
