using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopStacks;
class SelectionTests {
 static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
 [STAThread]static int Main(string[] args){var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};StackWindow window=null;try{
  string binary=Path.GetFullPath(args[0]),fixtures=Path.GetFullPath(args[1]);window=new StackWindow(binary,true){ShowActivated=false,Left=-20000,Top=-20000};
  typeof(StackWindow).GetField("captureMode",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(window,true);
  typeof(StackWindow).GetField("recycler",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(window,new RecycleService(Directory.GetDirectories(fixtures)));
  window.Show();string group=Path.Combine(binary,"收纳文件夹","左边栏");window.ShowPage(group,false);window.FinishMotion();var entries=window.ReadItems(group);Assert(entries.Count==2,"Synthetic selection fixture count.");
  window.ToggleSelectionMode();window.ChooseForTest(0);Assert(window.SelectedCount==1&&window.LastOpened=="","Selection must not launch an item.");
  window.RequestRecycleSelection();Assert(window.DeleteConfirmationVisible&&window.TestRecycleRequests==0,"Deletion requires inline confirmation.");
  Capture(window,Path.Combine(binary,"selection-confirmation.png"));
  var escape=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent};window.RaiseEvent(escape);Assert(!window.DeleteConfirmationVisible&&window.CurrentPath==group&&window.SelectedCount==1,"Esc first cancels confirmation without navigation.");
  window.ToggleSelectAll();Assert(window.SelectedCount==2,"Select all only includes eligible originals.");window.RequestRecycleSelection();window.ConfirmRecycleSelection();Assert(window.TestRecycleRequests==2&&!window.DeleteConfirmationVisible&&entries.All(i=>File.Exists(i.OriginalSource)||Directory.Exists(i.OriginalSource)),"Test-mode confirmation must never recycle real data.");
  string original=entries.First(i=>File.Exists(i.OriginalSource)).OriginalSource,offline=original+".selection-test-offline";File.Move(original,offline);try{Assert(window.ReadItems(group).Count==1,"Missing original disappears without rewriting navigation.");}finally{File.Move(offline,original);}Assert(window.ReadItems(group).Count==2,"Restored original reappears.");
  window.ShowPage("",false);Assert(window.SelectedCount==0&&!window.DeleteConfirmationVisible,"Home has no deletion selection.");
  Console.WriteLine("PASS: inline selection, no launch, confirmation cancel, multi-select, test guard, disappearing/restored originals and protected home.");return 0;
 }catch(Exception ex){Console.WriteLine("FAIL: "+ex.GetBaseException().Message);return 1;}finally{Program.Quitting=true;if(window!=null)window.Close();app.Shutdown();}}
 static void Capture(StackWindow window,string path){window.UpdateLayout();var root=(FrameworkElement)window.Content;var bitmap=new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),(int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(path))encoder.Save(stream);}
}
