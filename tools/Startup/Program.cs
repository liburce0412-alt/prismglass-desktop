using System;
using System.IO;
using System.Windows.Forms;

internal static class Program {
 [STAThread] static int Main(string[] args){
  try{return StartupTransition.Run(args);}
  catch(Exception error){
   string log=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"launcher-error.log");
   string message=error.GetBaseException().Message;
   try{File.WriteAllText(log,DateTimeOffset.Now+Environment.NewLine+error);message+=Environment.NewLine+log;}catch(IOException){}catch(UnauthorizedAccessException){}
   if(Array.IndexOf(args,"--login")<0)MessageBox.Show(message+"\n请重新打开 PrismGlass，或运行安装器修复。","PrismGlass 未能启动",MessageBoxButtons.OK,MessageBoxIcon.Error);
   return 1;
  }
 }
}
