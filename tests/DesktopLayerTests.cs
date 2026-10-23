using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopStacks;

// All windows belong to this test. Never touches the real desktop view or user files.
class DesktopLayerTests {
 [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window,uint command);
 [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
 static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
 static void Invoke(DesktopPresentation presentation,string method,params object[] args){typeof(DesktopPresentation).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(presentation,args);}
 static bool Above(IntPtr upper,IntPtr lower){IntPtr window=upper;for(int i=0;i<10000&&window!=IntPtr.Zero;i++,window=GetWindow(window,2))if(window==lower)return true;return false;}
 [STAThread] static int Main(string[] args){
  var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Window desktop=null,cover=null;DesktopPresentation presentation=null;
  try{
   desktop=new Window{Title="Synthetic desktop",Width=320,Height=200,Left=-10000,Top=-10000,ShowActivated=false,ShowInTaskbar=false};desktop.Show();IntPtr host=new WindowInteropHelper(desktop).Handle;
   SetWindowPos(host,new IntPtr(1),0,0,0,0,0x0001|0x0002|0x0010|0x0200);
   cover=new Window{Title="Synthetic document",Width=320,Height=200,Left=-10000,Top=-10000,ShowActivated=false,ShowInTaskbar=false,Background=Brushes.White};cover.Show();IntPtr document=new WindowInteropHelper(cover).Handle;
   presentation=new DesktopPresentation(args[0],delegate{});Invoke(presentation,"CreateEntry",host);
   var entry=(Window)typeof(DesktopPresentation).GetField("entry",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(presentation);IntPtr launcher=new WindowInteropHelper(entry).Handle;
   Assert(Above(document,launcher)&&Above(launcher,host),"Creation must place the launcher below documents and above its desktop host.");
   // Reproduces the old desktop-activation repair, which incorrectly used HWND_TOP.
   SetWindowPos(launcher,IntPtr.Zero,0,0,0,0,0x0001|0x0002|0x0010|0x0040);
   Assert(Above(document,launcher)&&Above(launcher,host),"A desktop-related raise must not cover an existing document.");
   entry.Activate();app.Dispatcher.Invoke(new Action(delegate{}),DispatcherPriority.Background);
   Assert(Above(document,launcher),"Focusing the launcher must not raise it above a document.");
   Assert((GetWindowLongPtr(launcher,-20).ToInt64()&8)==0,"The launcher must remain non-topmost.");
   entry.Left=420;entry.Top=280;Invoke(presentation,"SaveEntryPosition");
   var position=DesktopAutoCollector.ReadJson(Path.Combine(args[0],"桌面入口位置.json"),new DesktopEntryPosition());
   Invoke(presentation,"CloseEntry");Invoke(presentation,"CreateEntry",host);
   var restored=DesktopAutoCollector.ReadJson(Path.Combine(args[0],"桌面入口状态.json"),new DesktopEntryPosition());
   Assert(position.x==restored.x&&position.y==restored.y,"Layer enforcement must retain drag position persistence.");
   Console.WriteLine("PASS: desktop layer creation, raise, focus, non-topmost and position persistence.");return 0;
  }catch(Exception ex){Console.WriteLine("FAIL: "+ex.GetBaseException().Message);return 1;}
  finally{if(presentation!=null)presentation.Dispose();if(cover!=null)cover.Close();if(desktop!=null)desktop.Close();app.Shutdown();}
 }
}
