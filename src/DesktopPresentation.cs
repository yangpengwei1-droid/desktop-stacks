using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DesktopStacks {
 public class DesktopPresentationSettings { public bool enabled,restoreNoIcons;public string strategy="desktop-view-layer"; }
 public sealed class DesktopPresentation : IDisposable {
  readonly string directory;readonly Action open;DispatcherTimer timer;Window entry;IntPtr entryHandle,parent;bool ownsView,restoreNoIcons;long stamp=-1;
  public DesktopPresentation(string path,Action onOpen){directory=path;open=onOpen;}
  public static void Apply(bool noIcons){WithDesktop(delegate(object desktop){object document=null;try{document=desktop.GetType().InvokeMember("Document",System.Reflection.BindingFlags.GetProperty,null,desktop,null);uint flags=unchecked((uint)Convert.ToInt64(document.GetType().InvokeMember("FolderFlags",System.Reflection.BindingFlags.GetProperty,null,document,null)));flags=noIcons?flags|0x1000u:flags&~0x1000u;document.GetType().InvokeMember("FolderFlags",System.Reflection.BindingFlags.SetProperty,null,document,new object[]{flags});}finally{Release(document);}});}
  static void Release(object value){if(value!=null&&Marshal.IsComObject(value))try{Marshal.FinalReleaseComObject(value);}catch{}}
  static void WithDesktop(Action<object> action){object shell=null,windows=null,desktop=null;try{shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));windows=shell.GetType().InvokeMember("Windows",System.Reflection.BindingFlags.InvokeMethod,null,shell,null);object[] args={0,null,8,0,1};var byref=new System.Reflection.ParameterModifier(5);byref[3]=true;desktop=windows.GetType().InvokeMember("FindWindowSW",System.Reflection.BindingFlags.InvokeMethod,null,windows,args,new[]{byref},null,null);if(desktop==null)throw new IOException("Windows桌面尚未就绪。");action(desktop);}finally{Release(desktop);Release(windows);Release(shell);}}
  [ComImport,Guid("6D5140C1-7436-11CE-8034-00AA006009FA"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IService { [PreserveSig]int QueryService(ref Guid service,ref Guid iid,[MarshalAs(UnmanagedType.IUnknown)]out object result); }
  [ComImport,Guid("000214E2-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IBrowser {void GetWindow();void Help();void InsertMenus();void SetMenu();void RemoveMenus();void SetStatusText();void EnableModeless();void TranslateAccelerator();void BrowseObject();void GetViewStateStream();void GetControlWindow();void SendControlMsg();void QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)]out object view);}
  [ComImport,Guid("000214E3-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IView {void GetWindow(out IntPtr window);}
  [ComImport,Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IFolder {void GetCurrentViewMode();void SetCurrentViewMode();void GetFolder();void Item(int index,out IntPtr item);void ItemCount(uint flags,out int count);void Items();void GetSelectionMarkedItem();void GetFocusedItem();void GetItemPosition(IntPtr item,out POINT point);}
  [StructLayout(LayoutKind.Sequential)]struct POINT {public int X,Y;}
  [StructLayout(LayoutKind.Sequential)]struct RECT {public int Left,Top,Right,Bottom;}
  [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SHGetNameFromIDList(IntPtr item,uint kind,out IntPtr name);
  [DllImport("user32.dll")]static extern IntPtr GetParent(IntPtr window);
  [DllImport("user32.dll")]static extern bool IsWindow(IntPtr window);
  [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
  [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr window,out RECT rect);
  [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SetParent(IntPtr child,IntPtr parent);
  [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
  [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]static extern IntPtr SetWindowLongPtr(IntPtr window,int index,IntPtr value);
  [DllImport("user32.dll",SetLastError=true)]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
  [DllImport("kernel32.dll")]static extern void SetLastError(uint error);
  static IntPtr ReadHost(out int x,out int y){IntPtr host=IntPtr.Zero;int px=24,py=24;WithDesktop(delegate(object desktop){Guid service=new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837"),iid=typeof(IBrowser).GUID;object browser=null,view=null;try{Marshal.ThrowExceptionForHR(((IService)desktop).QueryService(ref service,ref iid,out browser));((IBrowser)browser).QueryActiveShellView(out view);IntPtr window;((IView)view).GetWindow(out window);host=GetParent(window);var folder=(IFolder)view;int count;folder.ItemCount(2,out count);for(int i=0;i<count;i++){IntPtr item,name;folder.Item(i,out item);try{if(SHGetNameFromIDList(item,0x80028000u,out name)>=0){string source;try{source=Marshal.PtrToStringUni(name);}finally{Marshal.FreeCoTaskMem(name);}if(source.EndsWith("\\Desktop Stacks.lnk",StringComparison.OrdinalIgnoreCase)){POINT point;folder.GetItemPosition(item,out point);px=point.X;py=point.Y;break;}}}finally{Marshal.FreeCoTaskMem(item);}}}finally{Release(view);Release(browser);}});x=px;y=py;return host;}
  public void Start(){Refresh();timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};timer.Tick+=delegate{Refresh();};timer.Start();}
  void Refresh(){
   try{
    string path=Path.Combine(directory,"桌面显示模式.json");long next=File.Exists(path)?File.GetLastWriteTimeUtc(path).Ticks:0;var settings=DesktopAutoCollector.ReadJson(path,new DesktopPresentationSettings());
    if(!settings.enabled){if(ownsView){CloseEntry();Apply(settings.restoreNoIcons);ownsView=false;}stamp=next;return;}
    int x,y;IntPtr host=ReadHost(out x,out y);if(host==IntPtr.Zero)throw new IOException("无法取得桌面显示层。");
    if(next==stamp&&host==parent&&entry!=null&&IsWindow(entryHandle))return;
    restoreNoIcons=settings.restoreNoIcons;CloseEntry();CreateEntry(host,x,y);Apply(true);ownsView=true;stamp=next;
   }catch(Exception ex){CloseEntry();try{Apply(false);}catch{}ownsView=false;try{File.WriteAllText(Path.Combine(directory,"桌面视图状态.txt"),"桌面入口未应用，已保留原文件可见："+ex.Message);}catch{}}
  }
  void CreateEntry(IntPtr host,int x,int y){
   entry=new Window{Title="Desktop Stacks · 桌面入口",Width=96,Height=100,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,ShowActivated=false,Topmost=false};
   var stack=new StackPanel{VerticalAlignment=VerticalAlignment.Center};var icon=new BitmapImage();icon.BeginInit();icon.CacheOption=BitmapCacheOption.OnLoad;icon.UriSource=new Uri(Path.Combine(directory,"入口图标","办公收纳.png"));icon.EndInit();icon.Freeze();stack.Children.Add(new Image{Source=icon,Width=48,Height=48,Margin=new Thickness(0,2,0,5)});
   var text=new TextBlock{Text="Desktop Stacks",FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=12,Foreground=Brushes.White,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap};text.Effect=new DropShadowEffect{Color=Colors.Black,BlurRadius=3,ShadowDepth=1,Opacity=.9};stack.Children.Add(text);
   var button=new Button{Content=stack,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Cursor=Cursors.Hand,ToolTip="双击打开 Desktop Stacks"};System.Windows.Automation.AutomationProperties.SetName(button,"Desktop Stacks 桌面入口");button.MouseDoubleClick+=delegate(object sender,MouseButtonEventArgs e){e.Handled=true;open();};button.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Enter||e.Key==Key.Space){e.Handled=true;open();}};entry.Content=button;
   entry.Show();entryHandle=new WindowInteropHelper(entry).Handle;long style=GetWindowLongPtr(entryHandle,-16).ToInt64();SetWindowLongPtr(entryHandle,-16,new IntPtr((style|0x40000000L)&~0x80000000L));SetLastError(0);IntPtr old=SetParent(entryHandle,host);int error=Marshal.GetLastWin32Error();if(old==IntPtr.Zero&&error!=0)throw new System.ComponentModel.Win32Exception(error);
   RECT bounds;GetClientRect(host,out bounds);x=Math.Max(0,Math.Min(x,bounds.Right-96));y=Math.Max(0,Math.Min(y,bounds.Bottom-140));SetWindowPos(entryHandle,IntPtr.Zero,x,y,96,100,0x0010|0x0040);parent=host;
   File.WriteAllText(Path.Combine(directory,"桌面入口状态.json"),new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{strategy="desktop-view-layer",parentedToDesktop=GetParent(entryHandle)==host,visible=IsWindowVisible(entryHandle),entryHandle=entryHandle.ToInt64(),parentHandle=host.ToInt64(),x=x,y=y,fileAttributesChanged=0}));
  }
  void CloseEntry(){if(entry!=null){try{entry.Close();}catch{}entry=null;}entryHandle=IntPtr.Zero;parent=IntPtr.Zero;}
  public void Dispose(){if(timer!=null)timer.Stop();CloseEntry();if(ownsView){try{Apply(restoreNoIcons);}catch{}ownsView=false;}}
 }
}
