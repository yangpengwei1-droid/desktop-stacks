using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using DesktopStacks;
class AutoCollectTests {
 static List<string> passed=new List<string>();
 static void Assert(bool ok,string name){if(!ok)throw new Exception(name);passed.Add(name);}
 static void Scan(DesktopAutoCollector collector){collector.ScanNow();Thread.Sleep(100);collector.ScanNow();}
 static bool Hidden(string path){return (File.GetAttributes(path)&FileAttributes.Hidden)!=0;}
 static byte[] Hash(string path){using(var sha=System.Security.Cryptography.SHA256.Create())using(var file=File.OpenRead(path))return sha.ComputeHash(file);}
 [STAThread] static int Main(string[] args) {try{return Run(args);}catch(Exception ex){File.WriteAllText(Path.Combine(args[0],"测试失败.txt"),ex.GetType().FullName+"\r\n"+ex.Message+"\r\n"+ex.StackTrace,Encoding.UTF8);Console.WriteLine("FAIL "+ex.Message);return 1;}}
 static int Run(string[] args) {
  string root=args[0],app=Path.Combine(root,"app"),desktop=Path.Combine(root,"desktop");Directory.CreateDirectory(app);Directory.CreateDirectory(desktop);
  Directory.CreateDirectory(Path.Combine(app,"原图标缓存"));File.Copy(args[1],Path.Combine(app,"图标提取.exe"));
  foreach(string group in new[]{"左边栏","中间栏","右边栏"})Directory.CreateDirectory(Path.Combine(app,"收纳文件夹",group));
  string original=Path.Combine(desktop,"原有项目.txt");File.WriteAllText(original,"baseline");
  string file=Path.Combine(desktop,"新合同.txt"),folder=Path.Combine(desktop,"保存文件夹"),secret=Path.Combine(desktop,"原有隐藏文件.txt"),temp=Path.Combine(desktop,"~$合同.docx"),part=Path.Combine(desktop,"未完成.crdownload"),office=Path.Combine(desktop,"Desktop Stacks.lnk"),system=Path.Combine(desktop,"系统项.txt");
  File.WriteAllText(file,"测试资料，原件不得移动或改写。");byte[] hash=Hash(file);DateTime written=File.GetLastWriteTimeUtc(file);Directory.CreateDirectory(folder);
  File.WriteAllText(secret,"hidden");File.SetAttributes(secret,FileAttributes.Hidden);File.WriteAllText(temp,"temp");File.WriteAllText(part,"partial");File.WriteAllText(office,"entry");File.WriteAllText(system,"system");File.SetAttributes(system,FileAttributes.System);
  string conflict=Path.Combine(app,"收纳文件夹","左边栏","新合同.txt.lnk");File.WriteAllText(conflict,"不得覆盖已有导航");
  string shortcut=Path.Combine(desktop,"原快捷方式.lnk");
  NativeShortcut.Create(shortcut,original,"保留参数",null,null);byte[] shortcutHash=Hash(shortcut);
  Assert(NativeShortcut.ReadTarget(shortcut)==original&&NativeShortcut.ReadArguments(shortcut)=="保留参数","Unicode快捷方式目标与参数可回读");
  using(var collector=new DesktopAutoCollector(app,desktop,new[]{original},TimeSpan.FromMilliseconds(60))) {
   collector.Configure(true,"左边栏",null);
   Assert(!collector.ScanNow()&&!Hidden(file),"首次检测等待文件保存稳定，不修改原属性");Scan(collector);
   var entries=DesktopAutoCollector.ReadEntries(app);Assert(entries.Count==3,"文件、目录和原快捷方式自动收纳");
   Assert(entries.All(e=>e.group=="左边栏"&&File.Exists(e.link)),"新入口只追加到指定分区");
   Assert(Hash(file).SequenceEqual(hash)&&File.GetLastWriteTimeUtc(file)==written&&!Hidden(file)&&Directory.Exists(folder),"原文件内容、修改时间和路径保持");
   Assert(Hash(shortcut).SequenceEqual(shortcutHash),"原快捷方式字节及参数不变");
   Assert(File.ReadAllText(conflict)=="不得覆盖已有导航"&&entries.Single(e=>e.path==file).link!=conflict,"同名导航避免覆盖");
   Assert(!Hidden(temp)&&!Hidden(part)&&!Hidden(office)&&!Hidden(original)&&!entries.Any(e=>e.path==secret||e.path==system),"跳过临时、未完成下载、系统、原隐藏和已有项目");
   string busy=Path.Combine(desktop,"正在保存.txt");
   using(var writer=new FileStream(busy,FileMode.CreateNew,FileAccess.Write,FileShare.Read)){writer.WriteByte(65);writer.Flush();Scan(collector);Assert(!Hidden(busy),"写入中的文件不修改属性");}
   Scan(collector);Assert(DesktopAutoCollector.ReadEntries(app).Any(e=>e.path==busy)&&!Hidden(busy),"写入结束后收纳且原文件保持可见");
   collector.Configure(false,null,null);string paused=Path.Combine(desktop,"暂停时新文件.txt");File.WriteAllText(paused,"pause");Scan(collector);Assert(!Hidden(paused)&&!DesktopAutoCollector.ReadEntries(app).Any(e=>e.path==paused),"暂停期间不收纳");
   collector.Configure(true,null,null);
  }
  string renamed=Path.Combine(desktop,"合同已改名.txt");File.Move(file,renamed);
  using(var collector=new DesktopAutoCollector(app,desktop,new[]{original},TimeSpan.FromMilliseconds(60))) {
   Scan(collector);var entry=DesktopAutoCollector.ReadEntries(app).Single(e=>e.path==renamed);Assert(File.Exists(entry.link)&&!DesktopAutoCollector.ReadEntries(app).Any(e=>e.path==file),"停机后改名通过Windows文件标识恢复原入口");
   File.SetAttributes(renamed,FileAttributes.Normal);File.WriteAllText(renamed,"同名另存更新");Scan(collector);Assert(!Hidden(renamed)&&File.ReadAllText(renamed)=="同名另存更新","同名另存后入口保持且不隐藏原件");
   string deletedLink=entry.link;File.Delete(renamed);collector.ScanNow();Thread.Sleep(6200);collector.ScanNow();Assert(!DesktopAutoCollector.ReadEntries(app).Any(e=>e.path==renamed)&&!File.Exists(deletedLink),"原件被用户删除后清理本工具入口");
   collector.Configure(null,"中间栏",null);string middle=Path.Combine(desktop,"更换分区后.txt");File.WriteAllText(middle,"middle");Scan(collector);Assert(DesktopAutoCollector.ReadEntries(app).Single(e=>e.path==middle).group=="中间栏","改变默认分区只影响后来新增项目");
   File.WriteAllText(Path.Combine(app,"自动收纳记录.json"),"invalid");string safe=Path.Combine(desktop,"异常保护.txt");File.WriteAllText(safe,"safe");bool rejected=false;try{Scan(collector);}catch{rejected=true;}Assert(rejected&&!Hidden(safe),"记录损坏时停止收纳并保留原件");
  }
  File.WriteAllText(Path.Combine(root,"隔离测试结果.json"),new JavaScriptSerializer().Serialize(new{passed=passed.Count,checks=passed,businessActionsExecuted=false}),Encoding.UTF8);Console.WriteLine("PASS "+passed.Count);return 0;
 }
}
