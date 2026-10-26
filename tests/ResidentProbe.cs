using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
class ResidentProbe {
 [DllImport("kernel32.dll")]static extern bool IsProcessInJob(IntPtr process,IntPtr job,out bool inside);
 [STAThread]static void Main(string[] args){
  if(args.Length<1)return;bool inside;IsProcessInJob(Process.GetCurrentProcess().Handle,IntPtr.Zero,out inside);
  File.WriteAllText(args[0],new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{pid=Process.GetCurrentProcess().Id,inJob=inside,arguments=args.Skip(1).ToArray()}));
  var until=DateTime.UtcNow.AddSeconds(15);while(DateTime.UtcNow<until&&!File.Exists(args[0]+".stop"))Thread.Sleep(100);
 }
}
