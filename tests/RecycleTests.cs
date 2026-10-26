using System;
using System.IO;
using DesktopStacks;
class RecycleTests {
 static void Assert(bool value,string message){if(!value)throw new Exception(message);}
 [STAThread]static int Main(string[] args){try{
  string root=Path.GetFullPath(args[0]),desktop=Path.Combine(root,"desktop");Directory.CreateDirectory(desktop);
  var service=new RecycleService(desktop);string target=Path.Combine(root,"shortcut-target.txt");File.WriteAllText(target,"Synthetic target must remain.");
  Assert(service.Problem(target)!=null,"Outside-root paths must be rejected.");Assert(service.Problem(desktop)!=null,"The desktop root itself must be rejected.");Assert(service.Problem("::{645FF040-5081-101B-9F08-00AA002F954E}")!=null,"System navigation must be rejected.");
  string system=Path.Combine(desktop,"desktop.ini");File.WriteAllText(system,"Synthetic protected item");Assert(service.Problem(system)!=null,"Protected desktop metadata must be rejected.");
  var guard=new RecycleService.RecycleSink(target);Assert(guard.PreDeleteItem(0,null)<0&&guard.RefusedPermanent,"A non-recycling native operation must be canceled.");
  string changed=Path.Combine(desktop,"changed.txt");File.WriteAllText(changed,"before");string stamp=RecycleService.Fingerprint(changed);File.WriteAllText(changed,"changed after confirmation");Assert(!service.Recycle(changed,IntPtr.Zero,stamp).Success&&File.Exists(changed),"Changed selections must require a new confirmation.");
  string shortcut=Path.Combine(desktop,"Desktop Stacks 删除测试-快捷方式.lnk");NativeShortcut.Create(shortcut,target,null,null,null);
  var recycled=service.Recycle(shortcut,IntPtr.Zero);Assert(recycled.Success,"Shortcut recycle: "+recycled.Error);Assert(!File.Exists(shortcut)&&File.ReadAllText(target)=="Synthetic target must remain.","Only the original shortcut is recycled, never its target.");Assert(!string.IsNullOrEmpty(recycled.RecyclePath),"The native callback must confirm an item was created in the Recycle Bin.");
  string folder=Path.Combine(desktop,"Desktop Stacks 删除测试-文件夹"),child=Path.Combine(folder,"child.txt");Directory.CreateDirectory(folder);File.WriteAllText(child,"Synthetic content");
  var directory=service.Recycle(folder,IntPtr.Zero);Assert(directory.Success&&!Directory.Exists(folder),"Folder recycle: "+directory.Error);Assert(File.Exists(Path.Combine(directory.RecyclePath,"child.txt")),"Folder contents must remain recoverable in the Recycle Bin.");
  string locked=Path.Combine(desktop,"locked.txt");File.WriteAllText(locked,"Preserve locked synthetic file");using(var writer=new FileStream(locked,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){var failed=service.Recycle(locked,IntPtr.Zero);Assert(!failed.Success&&File.Exists(locked),"A locked file must remain in place and report failure.");}
  Console.WriteLine("PASS: scope, system protection, permanent-delete guard, native shortcut/folder recycling, target preservation and failure retention.");return 0;
 }catch(Exception ex){Console.WriteLine("FAIL: "+ex.GetBaseException().Message);return 1;}}
}
