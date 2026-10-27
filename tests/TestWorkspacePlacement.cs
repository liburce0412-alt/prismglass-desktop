using System;
using System.Drawing;
using System.Reflection;

internal static class TestWorkspacePlacement {
 static MethodInfo calculate;static int checks;
 static Rectangle Place(Rectangle area,Rectangle screen,Rectangle dock,bool apps){return (Rectangle)calculate.Invoke(null,new object[]{area,screen,dock,apps});}
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 static Point Center(Rectangle area){return new Point(area.Left+area.Width/2,area.Top+area.Height/2);}
 static int Main(string[] args){try{
  if(args.Length!=1)throw new ArgumentException("Pass the compiled LiquidDesktop assembly path.");
  var assembly=Assembly.LoadFrom(System.IO.Path.GetFullPath(args[0]));calculate=assembly.GetType("WorkspacePlacement",true).GetMethod("Calculate",BindingFlags.Public|BindingFlags.Static);
  var primary=new Rectangle(0,0,2560,1440);var primaryWork=new Rectangle(0,0,2560,1400);var primaryDock=new Rectangle(860,1300,840,80);
  var apps=Place(primaryWork,primary,primaryDock,true);Check(apps.Bottom==primaryDock.Top-16,"primary application center stays above actual Dock with 16 px gap");Check(Center(apps).X==Center(primaryDock).X,"application center aligns with Dock horizontal center");Check(primaryWork.Contains(apps),"primary panel remains inside its work area");
  var secondary=new Rectangle(2560,-164,2560,1600);var secondaryWork=new Rectangle(2560,-164,2560,1560);var secondaryDock=new Rectangle(3290,1290,1100,84);
  var second=Place(secondaryWork,secondary,secondaryDock,true);Check(second.Bottom==secondaryDock.Top-16,"unequal secondary panel uses physical Dock Y and negative monitor origin");Check(Center(second).X==Center(secondaryDock).X&&secondaryWork.Contains(second),"125 percent screen stays on its own physical work area without another DPI multiplication");Check(second.Width==apps.Width&&second.Height==apps.Height,"placement does not reinterpret physical dimensions as logical pixels");
  var settings=Place(secondaryWork,secondary,secondaryDock,false);Check(Center(settings)==Center(secondaryWork),"settings retains working-area centered placement");Check(settings==Place(secondaryWork,secondary,primaryDock,false),"settings position does not depend on Dock");
  Check(Place(secondaryWork,secondary,primaryDock,true)==Place(secondaryWork,secondary,Rectangle.Empty,true),"neighboring monitor Dock is rejected");
  var left=Place(secondaryWork,secondary,new Rectangle(2560,1290,220,84),true);var right=Place(secondaryWork,secondary,new Rectangle(4890,1290,220,84),true);Check(left.Left==secondaryWork.Left&&right.Right==secondaryWork.Right,"side-aligned Docks clamp horizontally within target work area");
  var shallow=new Rectangle(-1200,40,480,320);var shallowDock=new Rectangle(-1100,300,220,50);var compact=Place(shallow,shallow,shallowDock,true);Check(shallow.Contains(compact)&&compact.Bottom==shallowDock.Top-16,"small work area shrinks panel without overlapping Dock");
  var taskbarArea=new Rectangle(2620,-164,2500,1600);var taskbarPanel=Place(taskbarArea,secondary,new Rectangle(2560,1290,220,84),true);Check(taskbarArea.Contains(taskbarPanel),"left taskbar exclusion is honored");
  Check(Place(Rectangle.Empty,Rectangle.Empty,Rectangle.Empty,true)==Rectangle.Empty,"invalid work area produces no off-screen geometry");
  Console.WriteLine("Workspace placement: "+checks+" checks passed (no windows created).");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
