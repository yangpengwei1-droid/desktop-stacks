using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

namespace DesktopStacks {
 public class DesktopAutoConfig { public bool enabled; public string group="左边栏"; public bool startWithWindows; }
 public class DesktopAutoEntry {
  public string id,path,link,group,icon,identity; public long added;
  public int attributes; public bool hidden;
 }
 public class DesktopAutoState { public int version=1; public List<DesktopAutoEntry> entries=new List<DesktopAutoEntry>(); }
 // Indexes and shortcuts only. The only write to an original is its Hidden attribute.
 public sealed class DesktopAutoCollector : IDisposable {
  public const string MutexName="Local\\DesktopStacksAutoStore";
  readonly string app,desktop,folders,stateFile,configFile;
  readonly HashSet<string> originals;
  readonly Dictionary<string,Observation> observations=new Dictionary<string,Observation>(StringComparer.OrdinalIgnoreCase);
  readonly TimeSpan settle;
  readonly object observationLock=new object();
  Timer timer; int busy; volatile bool disposed;
  public Action Changed; public string LastError="";
  class Observation { public string signature; public DateTime since; }
  public DesktopAutoCollector(string directory,string desktopDirectory,IEnumerable<string> knownSources,TimeSpan stableFor) {
   app=Path.GetFullPath(directory);desktop=Path.GetFullPath(desktopDirectory);folders=Path.Combine(app,"收纳文件夹");
   stateFile=Path.Combine(app,"自动收纳记录.json");configFile=Path.Combine(app,"自动收纳设置.json");
   originals=new HashSet<string>(knownSources??new string[0],StringComparer.OrdinalIgnoreCase);settle=stableFor;
  }
  public static T ReadJson<T>(string path,T fallback) {
   if(!File.Exists(path))return fallback;
   return new JavaScriptSerializer().Deserialize<T>(File.ReadAllText(path,Encoding.UTF8));
  }
  public static DesktopAutoConfig ReadConfig(string directory){return ReadJson(Path.Combine(directory,"自动收纳设置.json"),new DesktopAutoConfig());}
  public static List<DesktopAutoEntry> ReadEntries(string directory){return ReadJson(Path.Combine(directory,"自动收纳记录.json"),new DesktopAutoState()).entries;}
  static void SaveJson(string path,object value) {
   string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
   try {
    byte[] data=new UTF8Encoding(true).GetBytes(new JavaScriptSerializer().Serialize(value));
    using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){output.Write(data,0,data.Length);output.Flush(true);}
    if(File.Exists(path))File.Replace(temporary,path,path+".bak");else File.Move(temporary,path);
   }finally{if(File.Exists(temporary))File.Delete(temporary);}
  }
  static bool Enter(Mutex mutex){try{return mutex.WaitOne(10000);}catch(AbandonedMutexException){return true;}}
  public void Configure(bool? enabled,string group,bool? startup) {
   using(var mutex=new Mutex(false,MutexName)) {
    if(!Enter(mutex))throw new IOException("收纳正在处理，请稍后重试。");
    try {
     var config=ReadConfig(app);if(enabled.HasValue)config.enabled=enabled.Value;
     if(group!=null){if(!ValidGroup(group))throw new ArgumentException("请选择左边栏、中间栏或右边栏。");config.group=group;}
     if(startup.HasValue){SetLoginStartup(startup.Value);config.startWithWindows=startup.Value;}
     SaveJson(configFile,config);
    }finally{mutex.ReleaseMutex();}
   }
  }
  public void Start(){if(timer==null)timer=new Timer(delegate{Tick();},null,0,2000);}
  void Tick() {
   if(disposed||Interlocked.Exchange(ref busy,1)!=0)return;
   try{if(ScanNow()&&Changed!=null&&!disposed)Changed();}
   catch(Exception ex){ReportError(ex);}
   finally{Interlocked.Exchange(ref busy,0);}
  }
  void ReportError(Exception ex){LastError=ex.Message;try{File.WriteAllText(Path.Combine(app,"自动收纳状态.txt"),DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+"\r\n自动收纳本次未完成，原文件保留："+ex.Message,Encoding.UTF8);}catch{}}
  public static bool ValidGroup(string group){return new[]{"左边栏","中间栏","右边栏"}.Contains(group);}
  bool IsDesktopPath(string path){return string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)),desktop,StringComparison.OrdinalIgnoreCase);}
  bool IsOwnLink(string path){return ValidGroup(Path.GetFileName(Path.GetDirectoryName(path)))&&string.Equals(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(path))),folders,StringComparison.OrdinalIgnoreCase)&&Path.GetExtension(path).Equals(".lnk",StringComparison.OrdinalIgnoreCase);}
  static bool Exists(string path){return File.Exists(path)||Directory.Exists(path);}
  static bool Excluded(string path) {
   string name=Path.GetFileName(path),ext=Path.GetExtension(path).ToLowerInvariant();
   return name.Equals("Desktop Stacks.lnk",StringComparison.OrdinalIgnoreCase)||name.Equals("desktop.ini",StringComparison.OrdinalIgnoreCase)||name.StartsWith(".")||name.StartsWith("~$")||name.StartsWith("~")||name.EndsWith("~")||new[]{".tmp",".temp",".part",".partial",".download",".crdownload",".swp"}.Contains(ext);
  }
  bool Stable(string path) {
   var info=new FileInfo(path);string signature=File.GetLastWriteTimeUtc(path).Ticks.ToString()+"|"+(Directory.Exists(path)?"directory":info.Length.ToString());
   Observation observation;
   if(!observations.TryGetValue(path,out observation)||observation.signature!=signature){observations[path]=new Observation{signature=signature,since=DateTime.UtcNow};return false;}
   return DateTime.UtcNow-observation.since>=settle;
  }
  public bool ScanNow() {
   lock(observationLock)using(var mutex=new Mutex(false,MutexName)) {
    if(!Enter(mutex))return false;
    try {
     var config=ReadConfig(app);if(!config.enabled||!ValidGroup(config.group))return false;
     var state=ReadJson(stateFile,new DesktopAutoState());if(state==null||state.entries==null)throw new IOException("自动收纳记录不可用，未隐藏任何新文件。");
     foreach(var entry in state.entries)if(!IsDesktopPath(entry.path)||!IsOwnLink(entry.link))throw new IOException("收纳记录路径不在指定范围，已停止处理。");
     string[] paths=Directory.GetFileSystemEntries(desktop);var present=new HashSet<string>(paths,StringComparer.OrdinalIgnoreCase);
     var byPath=state.entries.ToDictionary(e=>e.path,StringComparer.OrdinalIgnoreCase);
     var byIdentity=state.entries.Where(e=>!present.Contains(e.path)&&!string.IsNullOrEmpty(e.identity)).GroupBy(e=>e.identity).ToDictionary(g=>g.Key,g=>g.First());
     bool changed=false;int additions=0;var retire=new List<string>();
     foreach(string path in paths) {
      try {
      if(originals.Contains(path)||Excluded(path))continue;
      DesktopAutoEntry entry;byPath.TryGetValue(path,out entry);
      FileAttributes attributes;try{attributes=File.GetAttributes(path);}catch(IOException){continue;}catch(UnauthorizedAccessException){continue;}
      if((attributes&FileAttributes.System)!=0)continue;
      if(entry!=null&&((attributes&FileAttributes.Hidden)!=0)&&File.Exists(entry.link))continue;
      string identity=Identity(path);
      if(entry==null&&!string.IsNullOrEmpty(identity))byIdentity.TryGetValue(identity,out entry);
      if(entry==null&&(attributes&FileAttributes.Hidden)!=0)continue;
      if(!Stable(path)||!NoWriter(path))continue;
      if(entry==null) {
       if(additions>=4)continue;
       entry=new DesktopAutoEntry{id=Guid.NewGuid().ToString("N"),path=path,group=config.group,attributes=(int)attributes,hidden=(attributes&FileAttributes.Hidden)!=0,identity=identity,added=DateTime.UtcNow.Ticks};
       entry.link=ChooseLink(path,entry.group);entry.icon=Path.Combine(app,"原图标缓存","自动_"+entry.id+".png");
       CreateNavigation(path,entry.link);try{ExtractIcon(path,entry.icon);}catch(Exception ex){ReportError(ex);}state.entries.Add(entry);
       try{SaveJson(stateFile,state);}catch{state.entries.Remove(entry);if(File.Exists(entry.link))File.Delete(entry.link);throw;}
       additions++;changed=true; // Recovery record and a usable shortcut exist before hiding.
      }else if(!string.Equals(entry.path,path,StringComparison.Ordinal)) {
       string oldLink=entry.link;string newLink=ChooseLink(path,entry.group);CreateNavigation(path,newLink);
       entry.path=path;entry.link=newLink;entry.identity=identity;SaveJson(stateFile,state);retire.Add(oldLink);changed=true;
      }else if(!File.Exists(entry.link)) {CreateNavigation(path,entry.link);changed=true;}
      if((attributes&FileAttributes.Hidden)==0){File.SetAttributes(path,attributes|FileAttributes.Hidden);Notify(path);changed=true;}
      }catch(IOException ex){ReportError(ex);}catch(UnauthorizedAccessException ex){ReportError(ex);}
     }
     // A missed rename while the app was closed is matched by Windows file identity above.
     foreach(var entry in state.entries.ToArray()) {
      if(Exists(entry.path)){observations.Remove("missing:"+entry.id);continue;}
      string missing="missing:"+entry.id;Observation seen;
      if(!observations.TryGetValue(missing,out seen)){observations[missing]=new Observation{since=DateTime.UtcNow};continue;}
      if(DateTime.UtcNow-seen.since<TimeSpan.FromSeconds(6))continue;
      state.entries.Remove(entry);retire.Add(entry.link);changed=true;
     }
     if(changed){SaveJson(stateFile,state);foreach(string link in retire)if(IsOwnLink(link)&&File.Exists(link))File.Delete(link);}
     foreach(string observed in observations.Keys.Where(p=>!p.StartsWith("missing:")&&!present.Contains(p)).ToArray())observations.Remove(observed);
     return changed;
    }finally{mutex.ReleaseMutex();}
   }
  }
  string ChooseLink(string source,string group) {
   string directory=Path.Combine(folders,group);Directory.CreateDirectory(directory);
   string leaf=Path.GetFileName(source);if(Path.GetExtension(source).Equals(".lnk",StringComparison.OrdinalIgnoreCase))leaf=Path.GetFileNameWithoutExtension(source);
   if(leaf.Length>120)leaf=leaf.Substring(0,120);
   string result=Path.Combine(directory,leaf+".lnk");int suffix=2;
   while(File.Exists(result))result=Path.Combine(directory,leaf+"（自动收纳"+(suffix++)+"）.lnk");return result;
  }
  static void CreateNavigation(string source,string destination) {
   if(Path.GetExtension(source).Equals(".lnk",StringComparison.OrdinalIgnoreCase)){File.Copy(source,destination,false);File.SetAttributes(destination,FileAttributes.Normal);return;}
   NativeShortcut.Create(destination,source,null,null,null);
  }
  void ExtractIcon(string source,string destination) {
   string helper=Path.Combine(app,"图标提取.exe");if(!File.Exists(helper))return;
   bool thumbnail=Directory.Exists(source)||new[]{".png",".jpg",".jpeg"}.Contains(Path.GetExtension(source).ToLowerInvariant());
   using(var process=Process.Start(new ProcessStartInfo(helper,"\""+source+"\" \""+destination+"\" "+(thumbnail?"thumbnail":"icon")+" 48"){UseShellExecute=false,CreateNoWindow=true})) {
    if(!process.WaitForExit(1500)){try{process.Kill();}catch{}}
   }
  }
  void SetLoginStartup(bool enabled) {
   string startup=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),"Desktop Stacks.lnk");
   if(!enabled){if(File.Exists(startup))File.Delete(startup);return;}
   NativeShortcut.Create(startup,Path.Combine(app,"DesktopStacks.exe"),"--background",app,Path.Combine(app,"入口图标","办公收纳.ico"));
  }
  [StructLayout(LayoutKind.Sequential)] struct FileData { public uint attributes;public System.Runtime.InteropServices.ComTypes.FILETIME created,accessed,written;public uint volume,sizeHigh,sizeLow,links,indexHigh,indexLow; }
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetFileInformationByHandle(SafeFileHandle file,out FileData data);
  static string Identity(string path){using(var file=CreateFile(path,0,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero)){FileData data;if(file.IsInvalid||!GetFileInformationByHandle(file,out data))return null;return data.volume.ToString("X8")+":"+data.indexHigh.ToString("X8")+data.indexLow.ToString("X8");}}
  // Open a read-capable handle without reading bytes; disallow concurrent writers.
  static bool NoWriter(string path){using(var file=CreateFile(path,0x80000000,5,IntPtr.Zero,3,0x02000000,IntPtr.Zero))return !file.IsInvalid;}
  [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern void SHChangeNotify(uint change,uint flags,string first,IntPtr second);
  static void Notify(string path){SHChangeNotify(0x800,5,path,IntPtr.Zero);SHChangeNotify(0x1000,5,Path.GetDirectoryName(path),IntPtr.Zero);}
  public void Dispose(){disposed=true;if(timer!=null){using(var complete=new ManualResetEvent(false)){if(timer.Dispose(complete))complete.WaitOne(20000);}timer=null;}}
 }
}
