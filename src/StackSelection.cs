using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace DesktopStacks {
 public partial class StackWindow {
  Button selectItems,selectAllItems,recycleItems,confirmRecycle,cancelRecycle;
  Border recyclePanel;TextBlock recycleDetails;
  bool selectionMode,deleteConfirmationOpen,deletionRunning;
  readonly HashSet<string> selectedSources=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  readonly Dictionary<Button,CheckBox> selectionMarks=new Dictionary<Button,CheckBox>();
  Item[] pendingRecycle=new Item[0];
  Dictionary<string,string> pendingFingerprints=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  RecycleService recycler=new RecycleService(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
  public int SelectedCount {get{return selectedSources.Count;}}
  public bool DeleteConfirmationVisible {get{return deleteConfirmationOpen;}}
  public int TestRecycleRequests;
  Button TextTool(string text,int width){return new Button{Content=text,Width=width,Height=32,Margin=new Thickness(3,0,3,0),Style=ButtonStyle(8)};}
  void InitializeSelectionTools(StackPanel tools){
   selectItems=TextTool("选择",56);selectItems.ToolTip="选择文件或文件夹";selectItems.Click+=delegate{ToggleSelectionMode();};tools.Children.Add(selectItems);
   selectAllItems=TextTool("全选",76);selectAllItems.Click+=delegate{ToggleSelectAll();};tools.Children.Add(selectAllItems);
   recycleItems=TextTool("删除 (0)",90);recycleItems.ToolTip="将选中项目移到回收站";recycleItems.Click+=delegate{RequestRecycleSelection();};tools.Children.Add(recycleItems);
   System.Windows.Automation.AutomationProperties.SetName(selectItems,"选择项目");System.Windows.Automation.AutomationProperties.SetName(selectAllItems,"全选项目");System.Windows.Automation.AutomationProperties.SetName(recycleItems,"删除选中项目");
  }
  void InitializeRecyclePanel(){
   surface.Children.Remove(footer);surface.RowDefinitions[2].Height=GridLength.Auto;
   var bottom=new StackPanel();Grid.SetRow(bottom,2);surface.Children.Add(bottom);
   recyclePanel=new Border{Visibility=Visibility.Collapsed,Background=Brush("#EEF1E8"),CornerRadius=new CornerRadius(12),Padding=new Thickness(14,10,14,10),Margin=new Thickness(0,7,0,5)};bottom.Children.Add(recyclePanel);
   var content=new StackPanel();recyclePanel.Child=content;content.Children.Add(new TextBlock{Text="将所选项目移到回收站？",FontSize=14,FontWeight=FontWeights.SemiBold});
   content.Children.Add(new TextBlock{Text="删除原桌面项目。文件夹包含其中内容；快捷方式只删除快捷方式本身。",FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,5)});
   recycleDetails=new TextBlock{FontSize=11,TextWrapping=TextWrapping.Wrap};content.Children.Add(new ScrollViewer{Content=recycleDetails,MaxHeight=85,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
   var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,7,0,0)};content.Children.Add(actions);
   cancelRecycle=TextTool("取消",64);cancelRecycle.Click+=delegate{CancelRecycleSelection();};actions.Children.Add(cancelRecycle);
   confirmRecycle=TextTool("移到回收站",110);confirmRecycle.Foreground=Brush("#783E35");confirmRecycle.Background=Brush("#E9DED5");confirmRecycle.Click+=delegate{ConfirmRecycleSelection();};actions.Children.Add(confirmRecycle);
   System.Windows.Automation.AutomationProperties.SetName(confirmRecycle,"确认移到回收站");bottom.Children.Add(footer);footer.MinHeight=27;
  }
  void PrepareSelectionPage(string path){
   if(!string.Equals(CurrentPath,path??"",StringComparison.OrdinalIgnoreCase)){selectionMode=false;selectedSources.Clear();CancelRecycleSelection(false);}
   selectionMarks.Clear();
  }
  bool CanSelect(Item item){return !item.Category&&recycler.Problem(item.OriginalSource)==null;}
  void AttachSelectionTile(Button button,Item item,FrameworkElement content){
   var frame=new Grid();frame.Children.Add(content);
   var mark=new CheckBox{HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(5),IsHitTestVisible=false,Focusable=false,Visibility=Visibility.Collapsed};frame.Children.Add(mark);button.Content=frame;selectionMarks[button]=mark;
  }
  void UpdateSelection(){
   bool home=string.IsNullOrEmpty(CurrentPath);selectItems.Visibility=home?Visibility.Collapsed:Visibility.Visible;selectItems.Content=selectionMode?"完成":"选择";
   selectAllItems.Visibility=recycleItems.Visibility=!home&&selectionMode?Visibility.Visible:Visibility.Collapsed;
   var eligible=items.Select(b=>(Item)b.Tag).Where(CanSelect).Select(i=>i.OriginalSource).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
   selectedSources.IntersectWith(eligible);recycleItems.Content="删除 ("+selectedSources.Count+")";recycleItems.IsEnabled=selectedSources.Count>0&&!deletionRunning;
   selectAllItems.Content=eligible.Length>0&&eligible.All(selectedSources.Contains)?"取消全选":"全选";
   selectItems.IsEnabled=selectAllItems.IsEnabled=back.IsEnabled=!deletionRunning;
   foreach(var pair in selectionMarks){var item=(Item)pair.Key.Tag;bool allowed=CanSelect(item),selected=selectedSources.Contains(item.OriginalSource);pair.Value.Visibility=selectionMode&&allowed?Visibility.Visible:Visibility.Collapsed;pair.Value.IsChecked=selected;pair.Key.IsEnabled=!deletionRunning&&(!selectionMode||allowed);pair.Key.Background=selected?Brush("#DDE8D7"):Brushes.Transparent;System.Windows.Automation.AutomationProperties.SetItemStatus(pair.Key,selected?"已选中":"未选中");}
   if(selectionMode&&!deleteConfirmationOpen&&!deletionRunning)footer.Text="已选 "+selectedSources.Count+" 项 · Delete 删除 · Esc 退出选择";
  }
  public void ToggleSelectionMode(){if(deletionRunning)return;selectionMode=!selectionMode;selectedSources.Clear();CancelRecycleSelection(false);if(!selectionMode){ShowPage(CurrentPath,false);return;}UpdateSelection();}
  public void ToggleSelectAll(){if(!selectionMode||deletionRunning||deleteConfirmationOpen)return;var all=items.Select(b=>(Item)b.Tag).Where(CanSelect).Select(i=>i.OriginalSource).ToArray();bool clear=all.All(selectedSources.Contains);selectedSources.Clear();if(!clear)foreach(string source in all)selectedSources.Add(source);UpdateSelection();}
  void HandleItemClick(Item item){
   if(deletionRunning||deleteConfirmationOpen)return;
   if(selectionMode){if(CanSelect(item)){if(!selectedSources.Add(item.OriginalSource))selectedSources.Remove(item.OriginalSource);UpdateSelection();}return;}
   if(item.Category)ShowPage(item.Path,false);else OpenItem(item);
  }
  void ShowItemMenu(Button button,Item item){
   var context=new ContextMenu{FontFamily=FontFamily,FontSize=13};context.Opened+=delegate{menuOpen=true;};context.Closed+=delegate{menuOpen=false;};
   if(!item.Category){
    var choose=new MenuItem{Header="选择此项",IsEnabled=CanSelect(item)};choose.Click+=delegate{selectionMode=true;selectedSources.Add(item.OriginalSource);UpdateSelection();};context.Items.Add(choose);
    var recycle=new MenuItem{Header="移到回收站…",IsEnabled=CanSelect(item)};recycle.Click+=delegate{if(!selectedSources.Contains(item.OriginalSource))selectedSources.Clear();selectedSources.Add(item.OriginalSource);selectionMode=true;UpdateSelection();RequestRecycleSelection();};context.Items.Add(recycle);context.Items.Add(new Separator());
   }
   var show=new MenuItem{Header=item.Category?"打开收纳文件夹":"在文件夹中显示"};show.Click+=delegate{Launch(item.Category?item.Path:System.IO.Path.GetDirectoryName(item.OriginalSource??item.Path));};context.Items.Add(show);context.PlacementTarget=button;context.IsOpen=true;
  }
  public void RequestRecycleSelection(){
   if(deletionRunning)return;pendingRecycle=items.Select(b=>(Item)b.Tag).Where(i=>selectedSources.Contains(i.OriginalSource)&&CanSelect(i)).GroupBy(i=>i.OriginalSource,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToArray();if(pendingRecycle.Length==0)return;
   try{pendingFingerprints=pendingRecycle.ToDictionary(i=>i.OriginalSource,i=>RecycleService.Fingerprint(i.OriginalSource),StringComparer.OrdinalIgnoreCase);}catch(Exception ex){pendingRecycle=new Item[0];footer.Text="无法确认选中项目："+ex.Message;return;}
   deleteConfirmationOpen=true;recycleDetails.Text=string.Join("\n\n",pendingRecycle.Select(i=>i.Name+"\n"+i.OriginalSource));recyclePanel.Visibility=Visibility.Visible;ResizeForRecyclePanel();cancelRecycle.Focus();
  }
  public void CancelRecycleSelection(){CancelRecycleSelection(true);}
  void CancelRecycleSelection(bool resize){deleteConfirmationOpen=false;pendingRecycle=new Item[0];if(recyclePanel!=null){recyclePanel.Visibility=Visibility.Collapsed;if(resize)ResizeForRecyclePanel();}}
  void ResizeForRecyclePanel(){if(string.IsNullOrEmpty(CurrentPath))return;Height=root.Height=Math.Min(Math.Max(BaseHeight,canvas.Height+147+(deleteConfirmationOpen?210:0)),Math.Max(240,activeWorkArea.Height-16));PlaceCurrentPage();}
  public void ConfirmRecycleSelection(){
   if(!deleteConfirmationOpen||deletionRunning||pendingRecycle.Length==0)return;
   if(TestMode){TestRecycleRequests+=pendingRecycle.Length;CancelRecycleSelection();footer.Text="测试模式未删除任何文件。";return;}
   Item[] batch=pendingRecycle.ToArray();var fingerprints=pendingFingerprints;IntPtr owner=new WindowInteropHelper(this).Handle;deletionRunning=true;confirmRecycle.IsEnabled=cancelRecycle.IsEnabled=false;footer.Text="正在移到回收站…";UpdateSelection();
   var worker=new Thread(delegate(){var results=new List<RecycleResult>();foreach(var item in batch)results.Add(recycler.Recycle(item.OriginalSource,owner,fingerprints[item.OriginalSource]));if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(delegate{
    deletionRunning=false;confirmRecycle.IsEnabled=cancelRecycle.IsEnabled=true;CancelRecycleSelection(false);
    foreach(var result in results.Where(r=>r.Success))selectedSources.Remove(result.Source);
    double offset=scroller.VerticalOffset;ShowPage(CurrentPath,false);FinishMotion();UpdateLayout();scroller.ScrollToVerticalOffset(offset);
    var failures=results.Where(r=>!r.Success).ToArray();footer.Text="已移到回收站 "+results.Count(r=>r.Success)+" 项"+(failures.Length==0?" · 可从Windows回收站恢复。":"；"+failures.Length+" 项未完成，已保留选择。" );
    footer.ToolTip=failures.Length==0?null:string.Join("\n",failures.Select(r=>System.IO.Path.GetFileName(r.Source)+"："+r.Error));
   }));});worker.SetApartmentState(ApartmentState.STA);worker.IsBackground=true;worker.Start();
  }
  bool HandleSelectionKey(KeyEventArgs e){
   if(deletionRunning){if(e.Key==Key.Delete||e.Key==Key.Escape||e.Key==Key.Back)return true;return false;}
   if(e.Key==Key.Escape||e.Key==Key.Back){if(deleteConfirmationOpen){CancelRecycleSelection();return true;}if(selectionMode){ToggleSelectionMode();return true;}return false;}
   if(selectionMode&&e.Key==Key.A&&(Keyboard.Modifiers&ModifierKeys.Control)!=0){if(items.Select(b=>(Item)b.Tag).Where(CanSelect).Any(i=>!selectedSources.Contains(i.OriginalSource)))ToggleSelectAll();return true;}
   if(e.Key==Key.Delete&&!string.IsNullOrEmpty(CurrentPath)){
    if(!selectionMode){var focused=items.FirstOrDefault(b=>b.IsKeyboardFocusWithin);if(focused==null||!CanSelect((Item)focused.Tag))return true;selectionMode=true;selectedSources.Add(((Item)focused.Tag).OriginalSource);UpdateSelection();}
    RequestRecycleSelection();return true;
   }
   return false;
  }
 }
}
