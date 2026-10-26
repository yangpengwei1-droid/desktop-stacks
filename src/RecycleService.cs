using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace DesktopStacks {
 public sealed class RecycleResult {public string Source,Error,RecyclePath;public bool Success;}
 public sealed class RecycleService {
  readonly string[] roots;
  public RecycleService(params string[] allowedRoots){roots=allowedRoots.Where(p=>!string.IsNullOrEmpty(p)).Select(p=>Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar)).ToArray();}
  public string Problem(string source){
   try{
    if(string.IsNullOrEmpty(source)||source.StartsWith("::{",StringComparison.Ordinal))return "系统位置不能删除。";
    string full=Path.GetFullPath(source);if(!roots.Contains(Path.GetDirectoryName(full),StringComparer.OrdinalIgnoreCase))return "仅可删除已收纳的桌面原项目。";
    string name=Path.GetFileName(full);if(name.Equals("desktop.ini",StringComparison.OrdinalIgnoreCase)||name.Equals("Desktop Stacks.lnk",StringComparison.OrdinalIgnoreCase))return "这是受保护的收纳或系统入口。";
    var attributes=File.GetAttributes(full);if((attributes&(FileAttributes.System|FileAttributes.ReparsePoint))!=0)return "系统项目或链接目录不支持在此删除。";
    var drive=new DriveInfo(Path.GetPathRoot(full));if(drive.DriveType!=DriveType.Fixed)return "此位置不支持可靠回收，原项目已保留。";
    return null;
   }catch(FileNotFoundException){return "原项目已不存在，请刷新列表。";}catch(DirectoryNotFoundException){return "原项目已不存在，请刷新列表。";}catch(Exception ex){return "无法确认原位置："+ex.Message;}
  }
  public static string Fingerprint(string source){return File.GetCreationTimeUtc(source).Ticks+"|"+File.GetLastWriteTimeUtc(source).Ticks+"|"+(Directory.Exists(source)?"directory":new FileInfo(source).Length.ToString());}
  public RecycleResult Recycle(string source,IntPtr owner,string expectedFingerprint=null){
   var result=new RecycleResult{Source=source};string problem=Problem(source);if(problem!=null){result.Error=problem;return result;}
   if(expectedFingerprint!=null){try{if(Fingerprint(source)!=expectedFingerprint){result.Error="确认期间项目发生变化，请重新选择后再删除。";return result;}}catch(Exception ex){result.Error=ex.Message;return result;}}
   object instance=null;IShellItem item=null;uint cookie=0;bool advised=false;
   try{
    Guid iid=typeof(IShellItem).GUID;Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(source,IntPtr.Zero,ref iid,out item));
    instance=new FileOperationObject();var operation=(IFileOperation)instance;var sink=new RecycleSink(source);
    // Force recycling, add Explorer undo, suppress external progress/error windows.
    operation.SetOperationFlags(0x00080000u|0x20000000u|0x00000040u|0x00000004u|0x00000010u|0x00000400u|0x00100000u);
    operation.SetOwnerWindow(owner);operation.Advise(sink,out cookie);advised=true;operation.DeleteItem(item,null);operation.PerformOperations();
    bool aborted;operation.GetAnyOperationsAborted(out aborted);
    result.Success=!aborted&&sink.Completed&&sink.ErrorCode>=0&&sink.Recycled;
    result.RecyclePath=sink.RecyclePath;
    if(!result.Success)result.Error=sink.RefusedPermanent?"此项目无法安全移入回收站，已取消删除。":sink.ErrorCode<0?Marshal.GetExceptionForHR(sink.ErrorCode).Message:"未完成移入回收站，原项目请以当前列表为准。";
   }catch(Exception ex){result.Error=ex.Message;}
   finally{if(advised&&instance!=null)try{((IFileOperation)instance).Unadvise(cookie);}catch{}if(item!=null)Marshal.FinalReleaseComObject(item);if(instance!=null)Marshal.FinalReleaseComObject(instance);}
   return result;
  }
  [ComImport,Guid("3AD05575-8857-4850-9277-11B85BDB8E09")]class FileOperationObject{}
  [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=true)]static extern int SHCreateItemFromParsingName(string path,IntPtr context,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out IShellItem item);
  [ComImport,Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]public interface IShellItem {
   void BindToHandler(IntPtr context,ref Guid handler,ref Guid iid,out IntPtr value);void GetParent(out IShellItem parent);void GetDisplayName(uint kind,out IntPtr name);void GetAttributes(uint mask,out uint attributes);void Compare(IShellItem other,uint hint,out int order);
  }
  [ComImport,Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IFileOperation {
   void Advise(IProgress sink,out uint cookie);void Unadvise(uint cookie);void SetOperationFlags(uint flags);void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)]string message);void SetProgressDialog(IntPtr dialog);void SetProperties(IntPtr properties);void SetOwnerWindow(IntPtr owner);
   void ApplyPropertiesToItem(IShellItem item);void ApplyPropertiesToItems(IntPtr items);void RenameItem(IShellItem item,[MarshalAs(UnmanagedType.LPWStr)]string name,IProgress sink);void RenameItems(IntPtr items,[MarshalAs(UnmanagedType.LPWStr)]string name);
   void MoveItem(IShellItem item,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)]string name,IProgress sink);void MoveItems(IntPtr items,IShellItem destination);void CopyItem(IShellItem item,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)]string name,IProgress sink);void CopyItems(IntPtr items,IShellItem destination);
   void DeleteItem(IShellItem item,IProgress sink);void DeleteItems(IntPtr items);void NewItem(IShellItem destination,uint attributes,[MarshalAs(UnmanagedType.LPWStr)]string name,[MarshalAs(UnmanagedType.LPWStr)]string template,IProgress sink);void PerformOperations();void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)]out bool aborted);
  }
  [ComVisible(true),Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]public interface IProgress {
   [PreserveSig]int StartOperations();[PreserveSig]int FinishOperations(int result);
   [PreserveSig]int PreRenameItem(uint flags,IShellItem item,[MarshalAs(UnmanagedType.LPWStr)]string name);[PreserveSig]int PostRenameItem(uint flags,IShellItem item,[MarshalAs(UnmanagedType.LPWStr)]string name,int result,IShellItem created);
   [PreserveSig]int PreMoveItem(uint flags,IShellItem item,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)]string name);[PreserveSig]int PostMoveItem(uint flags,IShellItem item,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)]string name,int result,IShellItem created);
   [PreserveSig]int PreCopyItem(uint flags,IShellItem item,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)]string name);[PreserveSig]int PostCopyItem(uint flags,IShellItem item,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)]string name,int result,IShellItem created);
   [PreserveSig]int PreDeleteItem(uint flags,IShellItem item);[PreserveSig]int PostDeleteItem(uint flags,IShellItem item,int result,IShellItem created);
   [PreserveSig]int PreNewItem(uint flags,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)]string name);[PreserveSig]int PostNewItem(uint flags,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)]string name,[MarshalAs(UnmanagedType.LPWStr)]string template,uint attributes,int result,IShellItem created);
   [PreserveSig]int UpdateProgress(uint total,uint completed);[PreserveSig]int ResetTimer();[PreserveSig]int PauseTimer();[PreserveSig]int ResumeTimer();
  }
  [ComVisible(true),ClassInterface(ClassInterfaceType.None)]public sealed class RecycleSink:IProgress {
   readonly string source;public bool Completed,Recycled,RefusedPermanent;public int ErrorCode;public string RecyclePath;
   public RecycleSink(string path){source=Path.GetFullPath(path);}
   public int PreDeleteItem(uint flags,IShellItem item){if((flags&0x80)==0){RefusedPermanent=true;return unchecked((int)0x80004004);}return 0;}
   public int PostDeleteItem(uint flags,IShellItem item,int result,IShellItem created){
    string current=Name(item);if(string.Equals(current,source,StringComparison.OrdinalIgnoreCase)){Completed=true;ErrorCode=result;Recycled=created!=null;if(created!=null)RecyclePath=Name(created);}return 0;
   }
   static string Name(IShellItem item){IntPtr text=IntPtr.Zero;try{item.GetDisplayName(0x80058000u,out text);return Marshal.PtrToStringUni(text);}catch{return null;}finally{if(text!=IntPtr.Zero)Marshal.FreeCoTaskMem(text);}}
   public int StartOperations(){return 0;}public int FinishOperations(int result){if(result<0)ErrorCode=result;return 0;}
   public int PreRenameItem(uint f,IShellItem i,string n){return 0;}public int PostRenameItem(uint f,IShellItem i,string n,int r,IShellItem c){return 0;}
   public int PreMoveItem(uint f,IShellItem i,IShellItem d,string n){return 0;}public int PostMoveItem(uint f,IShellItem i,IShellItem d,string n,int r,IShellItem c){return 0;}
   public int PreCopyItem(uint f,IShellItem i,IShellItem d,string n){return 0;}public int PostCopyItem(uint f,IShellItem i,IShellItem d,string n,int r,IShellItem c){return 0;}
   public int PreNewItem(uint f,IShellItem d,string n){return 0;}public int PostNewItem(uint f,IShellItem d,string n,string t,uint a,int r,IShellItem c){return 0;}
   public int UpdateProgress(uint t,uint c){return 0;}public int ResetTimer(){return 0;}public int PauseTimer(){return 0;}public int ResumeTimer(){return 0;}
  }
 }
}
