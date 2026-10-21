using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace DesktopStacks {
 public class Settings { public string mode="fan"; public string[] order=new string[0]; public Dictionary<string,string[]> entries; public Dictionary<string,int> columns; }
 public class Legacy { public string path; public bool hidden; }
 public class OriginalIcon { public string source,icon,stamp,displayName; public bool thumbnail; }
 public class Item { public string Name, Path, Target, Icon, OriginalSource; public bool Category,Thumbnail,Automatic; }
 public static class Native {
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(uint processId);
 }
 public class StackWindow : Window {
  public const double BaseWidth=840, BaseHeight=440;
  readonly string baseDir, foldersDir;
  readonly JavaScriptSerializer json=new JavaScriptSerializer();
  public Settings settings;
  readonly Grid root, surface;
  readonly Border paper;
  readonly TextBlock title, footer;
  readonly Button back, fan, grid;
  readonly Canvas canvas;
  readonly ScrollViewer scroller;
  readonly Dictionary<string,OriginalIcon> originalIcons;
  readonly DesktopAutoCollector automatic;
  readonly Dictionary<string,string> originalStamps=new Dictionary<string,string>();
  readonly HashSet<string> refreshingIcons=new HashSet<string>();
  readonly SemaphoreSlim iconWorker=new SemaphoreSlim(1);
  readonly List<Button> items=new List<Button>();
  readonly List<TransformGroup> transforms=new List<TransformGroup>();
  readonly Dictionary<string,ImageSource> iconCache=new Dictionary<string,ImageSource>();
  Storyboard motion;
  long revision;
  bool closing, menuOpen, captureMode;
  public bool TestMode;
  public string LastOpened="", CurrentPath="";
  public int ItemCount {get{return items.Count;}}
  public double MotionOpacity {get{return paper.Opacity;}}
  public ContextMenu menu;
  public Action ExitRequested;
  double originX=420,originY=412;
  Rect activeWorkArea=new Rect(0,0,1920,1040);
  Point activeAnchor=new Point(960,520);
  double contentWidth=720;
  readonly ScaleTransform paperScale=new ScaleTransform(1,1);

  static SolidColorBrush Brush(string color){var b=(SolidColorBrush)new BrushConverter().ConvertFromString(color);b.Freeze();return b;}
  public StackWindow(string directory, bool testing) {
   baseDir=directory; foldersDir=System.IO.Path.Combine(baseDir,"收纳文件夹"); TestMode=testing;
   settings=LoadSettings();
   originalIcons=File.Exists(System.IO.Path.Combine(baseDir,"原图标来源.json"))?json.Deserialize<Dictionary<string,OriginalIcon>>(File.ReadAllText(System.IO.Path.Combine(baseDir,"原图标来源.json"),Encoding.UTF8)):new Dictionary<string,OriginalIcon>();
   foreach(var reference in originalIcons.Values)originalStamps[reference.source]=reference.stamp;
   automatic=new DesktopAutoCollector(baseDir,Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),originalIcons.Values.Select(v=>v.source),TimeSpan.FromSeconds(3));
   automatic.Changed=delegate{if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(delegate{if(IsVisible&&!menuOpen&&!closing&&!string.IsNullOrEmpty(CurrentPath)){double offset=scroller.VerticalOffset;ShowPage(CurrentPath,false);FinishMotion();UpdateLayout();scroller.ScrollToVerticalOffset(offset);}}));};
   Width=BaseWidth;Height=BaseHeight;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
   AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;Topmost=true;
   Title="办公收纳";FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=13;Foreground=Brush("#29453A");
   UseLayoutRounding=true;SnapsToDevicePixels=true;
   root=new Grid{Width=BaseWidth,Height=BaseHeight,Background=Brushes.Transparent};Content=root;
   paper=new Border{Margin=new Thickness(22,20,22,28),Background=Brush("#FAFBF6"),CornerRadius=new CornerRadius(20)};
   paper.Effect=new DropShadowEffect{Color=Color.FromRgb(22,42,36),Opacity=.22,BlurRadius=24,ShadowDepth=7,Direction=270};
   paper.RenderTransform=paperScale;paper.RenderTransformOrigin=new Point(.5,1);root.Children.Add(paper);
   surface=new Grid{Margin=new Thickness(22,13,22,10)};paper.Child=surface;
   surface.RowDefinitions.Add(new RowDefinition{Height=new GridLength(45)});
   surface.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
   surface.RowDefinitions.Add(new RowDefinition{Height=new GridLength(27)});
   var header=new Grid();header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   back=SmallButton("返回上一层",Arrow(false));back.Margin=new Thickness(-5,0,3,0);back.Visibility=Visibility.Collapsed;back.Click+=delegate{ShowPage("",false);};header.Children.Add(back);
   title=new TextBlock{Text="工具箱",FontSize=16,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(title,1);header.Children.Add(title);
   var tools=new StackPanel{Orientation=Orientation.Horizontal};Grid.SetColumn(tools,2);header.Children.Add(tools);
   fan=SmallButton("扇形展开",FanGlyph());fan.Click+=delegate{SetMode("fan");};tools.Children.Add(fan);
   grid=SmallButton("网格展开",GridGlyph());grid.Click+=delegate{SetMode("grid");};tools.Children.Add(grid);
   var more=SmallButton("收纳选项",MoreGlyph());tools.Children.Add(more);
   var close=SmallButton("收起",CloseGlyph());close.Margin=new Thickness(8,0,0,0);tools.Children.Add(close);close.Click+=delegate{Dismiss();};
   menu=BuildMenu();more.ContextMenu=menu;more.Click+=delegate{menu.PlacementTarget=more;menu.IsOpen=true;};
   surface.Children.Add(header);
   canvas=new Canvas{ClipToBounds=false,Width=720};scroller=new ScrollViewer{Content=canvas,HorizontalContentAlignment=HorizontalAlignment.Center,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scroller,1);surface.Children.Add(scroller);
   footer=new TextBlock{Text="选择文件夹打开原位置",FontSize=11,Foreground=Brush("#5D7165"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};Grid.SetRow(footer,2);surface.Children.Add(footer);
   root.MouseDown+=delegate(object s,MouseButtonEventArgs e){if(e.OriginalSource==root)Dismiss();};
   PreviewKeyDown+=OnKey;
   Deactivated+=delegate{Dispatcher.BeginInvoke(new Action(delegate{if(!menuOpen&&!TestMode&&!captureMode&&IsVisible)Dismiss();}),DispatcherPriority.Background);};
   Closing+=delegate(object s,System.ComponentModel.CancelEventArgs e){if(!Program.Quitting){e.Cancel=true;Dismiss();}};
   KeyboardNavigation.SetTabNavigation(surface,KeyboardNavigationMode.Cycle);
   KeyboardNavigation.SetDirectionalNavigation(canvas,KeyboardNavigationMode.Cycle);
   ReadPlacementContext();
  }
  Settings LoadSettings(){try{return json.Deserialize<Settings>(File.ReadAllText(System.IO.Path.Combine(baseDir,"设置.json"),Encoding.UTF8))??new Settings();}catch{return new Settings();}}
  public void StartAutoCollection(){if(!TestMode)automatic.Start();}
  public void StopAutoCollection(){automatic.Dispose();}
  static bool IsNamespace(string source){return source!=null&&source.StartsWith("::{",StringComparison.Ordinal)&&source.EndsWith("}",StringComparison.Ordinal);}
  static bool OriginalExists(string source){return IsNamespace(source)||File.Exists(source)||Directory.Exists(source);}
  void SetMode(string value){settings.mode=value;try{File.WriteAllText(System.IO.Path.Combine(baseDir,"设置.json"),json.Serialize(settings),new UTF8Encoding(true));}catch{}ShowPage(CurrentPath,false);}
  public void SetTestMode(string value){settings.mode=value;ShowPage(CurrentPath,false);}
  FrameworkElement Arrow(bool right){return Glyph(right?"M 5,3 L 12,10 L 5,17":"M 12,3 L 5,10 L 12,17",18,20);}
  FrameworkElement CloseGlyph(){return Glyph("M 5,5 L 15,15 M 15,5 L 5,15",20,20);}
  FrameworkElement FanGlyph(){return Glyph("M 3,14 L 6,5 M 9,14 L 9,4 M 15,14 L 12,5",18,18);}
  FrameworkElement GridGlyph(){var c=new Canvas{Width=18,Height=18};foreach(int x in new[]{3,10})foreach(int y in new[]{3,10}){var r=new Rectangle{Width=5,Height=5,RadiusX=1,RadiusY=1,Fill=Brush("#3F6654")};Canvas.SetLeft(r,x);Canvas.SetTop(r,y);c.Children.Add(r);}return c;}
  FrameworkElement MoreGlyph(){var c=new Canvas{Width=20,Height=20};foreach(int x in new[]{3,9,15}){var d=new Ellipse{Width=3,Height=3,Fill=Brush("#3F6654")};Canvas.SetLeft(d,x);Canvas.SetTop(d,9);c.Children.Add(d);}return c;}
  FrameworkElement Glyph(string data,double w,double h){return new System.Windows.Shapes.Path{Data=Geometry.Parse(data),Stroke=Brush("#3F6654"),StrokeThickness=1.6,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Width=w,Height=h,Stretch=Stretch.None};}
  Button SmallButton(string label,object content){var b=new Button{Content=content,ToolTip=label,Width=32,Height=32,Margin=new Thickness(1,0,1,0),Padding=new Thickness(0),Style=ButtonStyle(8)};System.Windows.Automation.AutomationProperties.SetName(b,label);return b;}
  Style ButtonStyle(double radius){
   var st=new Style(typeof(Button));st.Setters.Add(new Setter(Button.BackgroundProperty,Brushes.Transparent));st.Setters.Add(new Setter(Button.BorderThicknessProperty,new Thickness(0)));st.Setters.Add(new Setter(Button.CursorProperty,Cursors.Hand));st.Setters.Add(new Setter(Button.ForegroundProperty,Brush("#29453A")));
   var border=new FrameworkElementFactory(typeof(Border));border.Name="frame";border.SetValue(Border.CornerRadiusProperty,new CornerRadius(radius));border.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)});
   var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center);presenter.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(presenter);
   var template=new ControlTemplate(typeof(Button)){VisualTree=border};
   var hover=new Trigger{Property=Button.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Button.BackgroundProperty,Brush("#E8EEE3")));template.Triggers.Add(hover);
   var focus=new Trigger{Property=Button.IsKeyboardFocusedProperty,Value=true};focus.Setters.Add(new Setter(Border.BorderBrushProperty,Brush("#527C63"),"frame"));focus.Setters.Add(new Setter(Border.BorderThicknessProperty,new Thickness(2),"frame"));template.Triggers.Add(focus);
   var disabled=new Trigger{Property=Button.IsEnabledProperty,Value=false};disabled.Setters.Add(new Setter(Button.OpacityProperty,.45));template.Triggers.Add(disabled);
   st.Setters.Add(new Setter(Button.TemplateProperty,template));return st;
  }
  ContextMenu BuildMenu(){
   var m=new ContextMenu{FontFamily=FontFamily,FontSize=13};m.Opened+=delegate{menuOpen=true;};m.Closed+=delegate{menuOpen=false;Dispatcher.BeginInvoke(new Action(delegate{if(!IsActive&&!TestMode&&!captureMode)Dismiss();}),DispatcherPriority.Background);};
   AddMenu(m,"打开收纳文件夹",delegate{Launch(foldersDir);});
   AddMenu(m,"隐藏已收纳桌面图标",delegate{ChangeLegacy(true);});
   AddMenu(m,"恢复已收纳桌面图标",delegate{ChangeLegacy(false);});
   m.Items.Add(new Separator());
   var collect=new MenuItem{Header="自动收纳桌面新文件",IsCheckable=true};
   collect.Click+=delegate{try{automatic.Configure(collect.IsChecked,null,null);footer.Text=collect.IsChecked?"新文件保存稳定后自动收纳，原路径保留。":"自动收纳已暂停，已有入口保留。";}catch(Exception ex){MessageBox.Show(this,ex.Message,"办公收纳");}};m.Items.Add(collect);
   var destination=new MenuItem{Header="新文件收纳到"};m.Items.Add(destination);
   foreach(string name in new[]{"左边栏","中间栏","右边栏"}){string group=name;var option=new MenuItem{Header=group+"末尾",Tag=group,IsCheckable=true};option.Click+=delegate{try{automatic.Configure(null,group,null);foreach(MenuItem sibling in destination.Items)sibling.IsChecked=(string)sibling.Tag==group;}catch(Exception ex){MessageBox.Show(this,ex.Message,"办公收纳");}};destination.Items.Add(option);}
   var startup=new MenuItem{Header="随 Windows 登录启动",IsCheckable=true};startup.Click+=delegate{try{automatic.Configure(null,null,startup.IsChecked);}catch(Exception ex){MessageBox.Show(this,ex.Message,"办公收纳");}};m.Items.Add(startup);
   m.Opened+=delegate{try{var config=DesktopAutoCollector.ReadConfig(baseDir);collect.IsChecked=config.enabled;startup.IsChecked=config.startWithWindows;foreach(MenuItem option in destination.Items)option.IsChecked=(string)option.Tag==config.group;}catch{collect.IsChecked=false;footer.Text="自动收纳设置无法读取，已有收纳仍可使用。";}};
   m.Items.Add(new Separator());AddMenu(m,"使用说明",delegate{Launch(System.IO.Path.Combine(baseDir,"使用说明.txt"));});
   AddMenu(m,"退出收纳",delegate{if(ExitRequested!=null)ExitRequested();});return m;
  }
  void AddMenu(ContextMenu m,string text,Action action){var item=new MenuItem{Header=text};item.Click+=delegate{action();};m.Items.Add(item);}
  void ChangeLegacy(bool hide){
   try{
    if(!hide)automatic.Configure(false,null,null);
    footer.Text=hide?"正在隐藏已收纳图标；公共桌面可能需要Windows确认。":"正在恢复已收纳图标；公共桌面可能需要Windows确认。";
    string exe=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","WindowsPowerShell","v1.0","powershell.exe");
    string arguments="-NoLogo -NoProfile -STA -ExecutionPolicy Bypass -File \""+System.IO.Path.Combine(baseDir,"桌面显示.ps1")+"\" -Action "+(hide?"Hide":"Restore")+" -AllowElevation";
    var process=Process.Start(new ProcessStartInfo(exe,arguments){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
    ThreadPool.QueueUserWorkItem(delegate{int code;using(process){process.WaitForExit();code=process.ExitCode;}if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(delegate{footer.Text=code==0?(hide?"已收纳桌面图标已隐藏，原位置未改变。":"已恢复桌面图标并暂停自动收纳，原位置未改变。"):"部分图标未处理，请检查Windows权限或状态记录。";if(code!=0)MessageBox.Show(this,"部分桌面图标未处理。请确认Windows管理员提示；详细结果在本目录桌面显示状态.json。","办公收纳");}));});
   }catch(Exception ex){MessageBox.Show(this,"处理桌面图标失败："+ex.Message,"办公收纳");}
  }
  ImageSource LoadIcon(string path){
   if(string.IsNullOrEmpty(path)||!File.Exists(path))path=System.IO.Path.Combine(baseDir,"原图标缓存","默认文件夹.png");
   if(iconCache.ContainsKey(path))return iconCache[path];
   try{
    var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.UriSource=new Uri(path,UriKind.Absolute);image.EndInit();image.Freeze();iconCache[path]=image;return image;
   }catch{return null;}
  }
  void ApplyOriginalIcon(Image image,Item item){
   image.Source=LoadIcon(item.Icon);
   string source=item.OriginalSource??item.Path;
   string stamp;try{stamp=IsNamespace(source)?"namespace":File.GetLastWriteTimeUtc(source).Ticks.ToString();if(item.Thumbnail&&Directory.Exists(item.Target))stamp+="|"+Directory.GetLastWriteTimeUtc(item.Target).Ticks.ToString();}catch{return;}
   string known;bool unchanged=originalStamps.TryGetValue(source,out known)&&known==stamp;
   if(unchanged&&File.Exists(item.Icon))return;
   if(!refreshingIcons.Add(source))return;
   string cache=item.Icon;
   if(string.IsNullOrEmpty(cache)){
    using(var sha=System.Security.Cryptography.SHA256.Create()){cache=System.IO.Path.Combine(baseDir,"原图标缓存",BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(source))).Replace("-","").Substring(0,20)+".png");}
   }
   ThreadPool.QueueUserWorkItem(delegate{
    iconWorker.Wait();bool success=false;
    try{
     bool thumbnail=item.Thumbnail||!IsNamespace(source)&&(Directory.Exists(source)||new[]{".png",".jpg",".jpeg"}.Contains(System.IO.Path.GetExtension(source).ToLowerInvariant()));
     var info=new ProcessStartInfo(System.IO.Path.Combine(baseDir,"图标提取.exe"),"\""+source+"\" \""+cache+"\" "+(item.Category?"icon 128":thumbnail?"thumbnail 48":"icon 48")){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
     using(var process=Process.Start(info)){success=process.WaitForExit(15000)&&process.ExitCode==0&&File.Exists(cache);}
    }catch{}finally{iconWorker.Release();}
    if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(delegate{refreshingIcons.Remove(source);if(success){iconCache.Remove(cache);image.Source=LoadIcon(cache);originalStamps[source]=stamp;}}));
   });
  }
  public List<Item> ReadItems(string path){
   var list=new List<Item>();string folder=string.IsNullOrEmpty(path)?foldersDir:path;
   if(!Directory.Exists(folder))return list;
   foreach(string dir in Directory.GetDirectories(folder)){if(System.IO.Path.GetFileName(dir).StartsWith("."))continue;OriginalIcon original;originalIcons.TryGetValue("folder:"+System.IO.Path.GetFileName(dir),out original);list.Add(new Item{Name=System.IO.Path.GetFileName(dir),Path=dir,Target=dir,OriginalSource=dir,Icon=original==null?null:original.icon,Category=true});}
   List<DesktopAutoEntry> automaticEntries;try{automaticEntries=DesktopAutoCollector.ReadEntries(baseDir);}catch{automaticEntries=new List<DesktopAutoEntry>();}
   var automaticLinks=new HashSet<string>(automaticEntries.Select(e=>e.link),StringComparer.OrdinalIgnoreCase);
    foreach(string link in Directory.GetFiles(folder,"*.lnk")){
     if(automaticLinks.Contains(link))continue;
     string target;try{target=NativeShortcut.ReadTarget(link);}catch{target=link;}
      string relative=link.Substring(foldersDir.Length+1).Replace('\\','/');string leaf=System.IO.Path.GetFileName(link);OriginalIcon original;if(!originalIcons.TryGetValue(relative,out original))originalIcons.TryGetValue(leaf,out original);
      if(original==null){var matches=originalIcons.Where(kv=>kv.Key.EndsWith("/"+leaf,StringComparison.Ordinal)).Select(kv=>kv.Value).ToArray();if(matches.Length==1)original=matches[0];}
      list.Add(new Item{Name=original!=null&&!string.IsNullOrEmpty(original.displayName)?original.displayName:System.IO.Path.GetFileNameWithoutExtension(link),Path=link,Target=target,OriginalSource=original==null?link:original.source,Icon=original==null?null:original.icon,Thumbnail=original!=null&&original.thumbnail,Category=false});
    }
   if(string.IsNullOrEmpty(path))return list.OrderBy(i=>{int n=Array.IndexOf(new[]{"左边栏","中间栏","右边栏"},i.Name);return n<0?999:n;}).ThenBy(i=>i.Name,StringComparer.CurrentCulture).ToList();
   string[] order;string group=System.IO.Path.GetFileName(folder);
   var sorted=settings.entries!=null&&settings.entries.TryGetValue(group,out order)?list.OrderBy(i=>{int n=Array.IndexOf(order,System.IO.Path.GetFileName(i.Path));return n<0?9999:n;}).ThenBy(i=>i.Name,StringComparer.CurrentCulture):list.OrderBy(i=>i.Name,StringComparer.CurrentCulture);
   list=sorted.ToList();
   foreach(var entry in automaticEntries.Where(e=>e.group==group&&OriginalExists(e.path)).OrderBy(e=>e.added))list.Add(new Item{Name=System.IO.Path.GetExtension(entry.path).Equals(".lnk",StringComparison.OrdinalIgnoreCase)?System.IO.Path.GetFileNameWithoutExtension(entry.path):System.IO.Path.GetFileName(entry.path),Path=entry.link,Target=entry.path,OriginalSource=entry.path,Icon=entry.icon,Thumbnail=Directory.Exists(entry.path),Automatic=true});
   return list;
  }
  public void ShowPage(string path,bool initial){
   revision++;if(motion!=null){motion.Remove(this);motion=null;}
   CurrentPath=path??"";closing=false;paper.Opacity=1;paperScale.ScaleX=paperScale.ScaleY=1;
   items.Clear();transforms.Clear();canvas.Children.Clear();scroller.ScrollToTop();
   bool home=string.IsNullOrEmpty(CurrentPath);bool useFan=home&&settings.mode=="fan";
   root.LayoutTransform=Transform.Identity;
   Width=root.Width=Math.Min(home?BaseWidth:1280,Math.Max(320,activeWorkArea.Width-16));
   contentWidth=Math.Max(200,root.Width-120);canvas.Width=contentWidth;
   title.Text=home?"工具箱":System.IO.Path.GetFileName(CurrentPath);back.Visibility=home?Visibility.Collapsed:Visibility.Visible;
   fan.Visibility=grid.Visibility=home?Visibility.Visible:Visibility.Collapsed;
   fan.Background=settings.mode=="fan"?Brush("#E1EADB"):Brushes.Transparent;grid.Background=settings.mode=="grid"?Brush("#E1EADB"):Brushes.Transparent;
   footer.Text=home?"按桌面左、中、右区域收纳 · Esc 收起":"点击打开原位置 · Esc 返回上一层";
   var entries=ReadItems(CurrentPath);if(entries.Count>8)useFan=false;
   if(!home)footer.Text=entries.Count+(entries.Any(i=>i.Automatic)?" 项 · 原有顺序保留，新文件追加末尾 · Esc 返回上一层":" 项 · 保留原桌面顺序 · Esc 返回上一层");
   if(entries.Count==0){canvas.Children.Add(new TextBlock{Text="这里还没有入口。可从选项打开收纳文件夹，加入快捷方式。",Margin=new Thickness(30,80,30,0),TextWrapping=TextWrapping.Wrap});return;}
   int cols=4;int configured;if(!home&&settings.columns!=null&&settings.columns.TryGetValue(System.IO.Path.GetFileName(CurrentPath),out configured))cols=Math.Max(1,Math.Min(6,configured));
   double columnStep=contentWidth/cols;double tileWidth=home?168:columnStep-8;double textWidth=tileWidth-10;double gap=176;
   if(useFan)gap=Math.Min(176,(contentWidth-20)/entries.Count);
   var rowHeights=new List<double>();var rowTops=new List<double>();double nextTop=7;
   for(int start=0;start<entries.Count;start+=cols){
    double maxLabel=17;
    for(int k=start;k<Math.Min(start+cols,entries.Count);k++){var measured=new FormattedText(entries[k].Name,System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface(FontFamily,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal),12.5,Brushes.Black,1.0){MaxTextWidth=textWidth};maxLabel=Math.Max(maxLabel,measured.Height);}
    double rowHeight=48+5+Math.Min(68,maxLabel)+16;rowTops.Add(nextTop);rowHeights.Add(rowHeight);nextTop+=rowHeight+7;
   }
   canvas.Height=home?270:Math.Max(260,Math.Ceiling(nextTop+1));
   Height=root.Height=Math.Min(home?BaseHeight:Math.Max(BaseHeight,canvas.Height+147),Math.Max(240,activeWorkArea.Height-16));
   PlaceCurrentPage();
   for(int i=0;i<entries.Count;i++){
    Item item=entries[i];double x,y;double iconSize=home?92:48;
    if(useFan){x=(contentWidth-(entries.Count*gap))/2+i*gap;y=44+Math.Pow(i-(entries.Count-1)/2.0,2)*4.5;}
    else if(home){x=(contentWidth-entries.Count*176)/2+i*176;y=48;}
    else{x=4+(i%cols)*columnStep;y=rowTops[i/cols];}
    var button=new Button{Width=tileWidth,Height=home?146:rowHeights[i/cols],Style=ButtonStyle(13),ToolTip=item.Name+"\n"+(item.Category?"打开此收纳文件夹":IsNamespace(item.OriginalSource)?"Windows系统位置":item.Target),Tag=item};
    var stack=new StackPanel{Width=tileWidth-10,VerticalAlignment=VerticalAlignment.Center};
    var image=new Image{Width=iconSize,Height=iconSize,Stretch=Stretch.Uniform};ApplyOriginalIcon(image,item);
    stack.Children.Add(image);stack.Children.Add(new TextBlock{Text=item.Name,FontSize=12.5,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,MaxHeight=68,Margin=new Thickness(2,5,2,0),LineHeight=17});button.Content=stack;
    button.GotKeyboardFocus+=delegate{button.BringIntoView();};
    System.Windows.Automation.AutomationProperties.SetName(button,item.Name);
    button.Click+=delegate(object sender,RoutedEventArgs e){e.Handled=true;var selected=(Item)((Button)sender).Tag;if(selected.Category)ShowPage(selected.Path,false);else OpenItem(selected);};
    button.MouseRightButtonUp+=delegate(object sender,MouseButtonEventArgs e){var selected=(Item)((Button)sender).Tag;Launch(selected.Category?selected.Path:System.IO.Path.GetDirectoryName(selected.Path));e.Handled=true;};
    Canvas.SetLeft(button,x);Canvas.SetTop(button,y);canvas.Children.Add(button);items.Add(button);
    var trans=new TransformGroup();trans.Children.Add(new ScaleTransform(1,1));trans.Children.Add(new TranslateTransform(0,0));button.RenderTransformOrigin=new Point(.5,.5);button.RenderTransform=trans;transforms.Add(trans);
   }
   BeginOpen(initial,useFan);
   RecordState();
   if(!captureMode&&items.Count>0)Dispatcher.BeginInvoke(new Action(delegate{items[0].Focus();}),DispatcherPriority.Input);
  }
  void Add(Storyboard s,DependencyObject target,DependencyProperty property,double from,double to,int duration,int delay){
   var a=new DoubleAnimation(from,to,TimeSpan.FromMilliseconds(duration)){BeginTime=TimeSpan.FromMilliseconds(delay),EasingFunction=new ExponentialEase{Exponent=5,EasingMode=EasingMode.EaseOut}};
   DependencyObject owner=target;PropertyPath path=new PropertyPath(property);
   if(object.ReferenceEquals(target,paperScale)){owner=paper;path=new PropertyPath("(0).(1)",UIElement.RenderTransformProperty,property);}
   else if(target is Transform){
    for(int i=0;i<transforms.Count;i++)for(int j=0;j<transforms[i].Children.Count;j++)if(object.ReferenceEquals(target,transforms[i].Children[j])){
     owner=items[i];path=new PropertyPath("(0).(1)["+j+"].(2)",UIElement.RenderTransformProperty,TransformGroup.ChildrenProperty,property);
    }
   }
   Storyboard.SetTarget(a,owner);Storyboard.SetTargetProperty(a,path);s.Children.Add(a);
  }
  bool ReduceMotion {get{return !SystemParameters.ClientAreaAnimation;}}
  void BeginOpen(bool initial,bool useFan){
   motion=new Storyboard();bool reduced=ReduceMotion;int duration=reduced?75:initial?320:210;
   paper.Opacity=initial?.3:1;paperScale.ScaleX=paperScale.ScaleY=initial&&!reduced?.94:1;
   Add(motion,paper,UIElement.OpacityProperty,initial?.3:1,1,duration,0);
   if(initial&&!reduced){Add(motion,paperScale,ScaleTransform.ScaleXProperty,.94,1,duration,0);Add(motion,paperScale,ScaleTransform.ScaleYProperty,.94,1,duration,0);}
   for(int i=0;i<items.Count;i++){
    var scale=(ScaleTransform)transforms[i].Children[0];var move=(TranslateTransform)transforms[i].Children[1];
    int delay=reduced?0:Math.Min(i*12,60);double x=Canvas.GetLeft(items[i]),y=Canvas.GetTop(items[i]);
    items[i].Opacity=.1;
    scale.ScaleX=scale.ScaleY=reduced?1:initial?.22:.86;
    move.X=reduced?0:initial?contentWidth/2-x-items[i].Width/2:0;
    move.Y=reduced?0:initial?236-y:14;
    Add(motion,items[i],UIElement.OpacityProperty,.1,1,duration,delay);
    if(!reduced){
     Add(motion,scale,ScaleTransform.ScaleXProperty,initial?.22:.86,1,duration,delay);Add(motion,scale,ScaleTransform.ScaleYProperty,initial?.22:.86,1,duration,delay);
     Add(motion,move,TranslateTransform.XProperty,initial?contentWidth/2-x-items[i].Width/2:0,0,duration,delay);
     Add(motion,move,TranslateTransform.YProperty,initial?236-y:14,0,duration,delay);
    }
   }
   motion.Begin(this,true);
  }
  public void FinishMotion(){if(motion!=null)motion.SeekAlignedToLastTick(this,TimeSpan.FromMilliseconds(600),TimeSeekOrigin.BeginTime);UpdateLayout();}
  public void Dismiss(){
   if(closing||!IsVisible)return;closing=true;long ticket=++revision;
   double paperOpacity=paper.Opacity,scaleX=paperScale.ScaleX,scaleY=paperScale.ScaleY;
   var positions=new List<double[]>();
   for(int i=0;i<items.Count;i++){var s=(ScaleTransform)transforms[i].Children[0];var t=(TranslateTransform)transforms[i].Children[1];positions.Add(new[]{s.ScaleX,s.ScaleY,t.X,t.Y,items[i].Opacity});}
   if(motion!=null)motion.Remove(this);motion=new Storyboard();int duration=ReduceMotion?60:140;
   paper.Opacity=paperOpacity;paperScale.ScaleX=scaleX;paperScale.ScaleY=scaleY;
   Add(motion,paper,UIElement.OpacityProperty,paperOpacity,0,duration,0);
   if(!ReduceMotion){
    Add(motion,paperScale,ScaleTransform.ScaleXProperty,scaleX,.96,duration,0);Add(motion,paperScale,ScaleTransform.ScaleYProperty,scaleY,.96,duration,0);
    for(int i=0;i<items.Count;i++){
     var s=(ScaleTransform)transforms[i].Children[0];var t=(TranslateTransform)transforms[i].Children[1];var p=positions[i];
     s.ScaleX=p[0];s.ScaleY=p[1];t.X=p[2];t.Y=p[3];items[i].Opacity=p[4];
     Add(motion,s,ScaleTransform.ScaleXProperty,p[0],.22,duration,0);Add(motion,s,ScaleTransform.ScaleYProperty,p[1],.22,duration,0);
     Add(motion,t,TranslateTransform.XProperty,p[2],contentWidth/2-Canvas.GetLeft(items[i])-items[i].Width/2,duration,0);Add(motion,t,TranslateTransform.YProperty,p[3],236-Canvas.GetTop(items[i]),duration,0);
     Add(motion,items[i],UIElement.OpacityProperty,p[4],0,duration,0);
    }
   }
   motion.Completed+=delegate{if(ticket==revision){Hide();closing=false;RecordState();}};motion.Begin(this,true);
  }
  void RecordState(){if(string.IsNullOrEmpty(Program.StatusFile))return;try{File.WriteAllText(Program.StatusFile,json.Serialize(new{processId=Process.GetCurrentProcess().Id,visible=IsVisible,active=IsActive,closing=closing,category=CurrentPath,mode=settings.mode}),Encoding.UTF8);}catch{}}
  public void HideImmediately(){revision++;if(motion!=null)motion.Remove(this);Hide();closing=false;}
  public void Toggle(){if(IsVisible&&!closing)Dismiss();else ShowStack();}
  public void ShowStack(){
   PositionAtCursor();if(!IsVisible)Show();Activate();Native.SetForegroundWindow(new WindowInteropHelper(this).Handle);ShowPage("",true);
  }
  public static Rect Place(Rect work,Point pointer,Size size){
   double x=Math.Max(work.Left+8,Math.Min(pointer.X-size.Width/2,work.Right-size.Width-8));
   double y=pointer.Y-work.Top>=size.Height+10?pointer.Y-size.Height+10:pointer.Y+10;
   y=Math.Max(work.Top+8,Math.Min(y,work.Bottom-size.Height-8));return new Rect(x,y,size.Width,size.Height);
  }
  void ReadPlacementContext(){
   Native.POINT point;Native.GetCursorPos(out point);var screen=Forms.Screen.FromPoint(new System.Drawing.Point(point.X,point.Y));
   double factor=1;
   try{var source=PresentationSource.FromVisual(this);if(source!=null)factor=source.CompositionTarget.TransformToDevice.M11;else{using(var g=System.Drawing.Graphics.FromHwnd(IntPtr.Zero))factor=g.DpiX/96.0;}}catch{}
   activeWorkArea=new Rect(screen.WorkingArea.X/factor,screen.WorkingArea.Y/factor,screen.WorkingArea.Width/factor,screen.WorkingArea.Height/factor);
   activeAnchor=new Point(point.X/factor,point.Y/factor);
  }
  void PositionAtCursor(){ReadPlacementContext();PlaceCurrentPage();}
  void PlaceCurrentPage(){
   if(captureMode)return;
   var p=Place(activeWorkArea,activeAnchor,new Size(Width,Height));Left=p.X;Top=p.Y;
   originX=activeAnchor.X-Left;originY=activeAnchor.Y-Top;
   paper.RenderTransformOrigin=new Point(Math.Max(0,Math.Min(1,originX/Width)),originY<Height/2?0:1);
  }
  void OnKey(object sender,KeyEventArgs e){if(e.Key==Key.Escape||e.Key==Key.Back){Escape();e.Handled=true;}}
  public void Escape(){if(!string.IsNullOrEmpty(CurrentPath))ShowPage("",false);else Dismiss();}
  public void ChooseForTest(int i){items[i].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
  void OpenItem(Item item){
   if(closing)return;
   string original=item.OriginalSource??item.Path;
   if(TestMode){LastOpened=original;return;}
   if(!OriginalExists(original)){MessageBox.Show(this,"原位置当前不可用：\n"+original+"\n请确认磁盘已连接或原路径未改名。","办公收纳");return;}
   Dismiss();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(155)};timer.Tick+=delegate{timer.Stop();Launch(original);};timer.Start();
  }
  void Launch(string path){if(TestMode){LastOpened=path;return;}try{Process.Start(IsNamespace(path)?new ProcessStartInfo("explorer.exe","shell:"+path){UseShellExecute=true}:new ProcessStartInfo(path){UseShellExecute=true});Dismiss();}catch(Exception ex){MessageBox.Show(this,"未能打开："+ex.Message,"办公收纳");}}
  public void CaptureView(string destination,string page,string mode,bool frames,bool bottom=false){
   captureMode=true;TestMode=true;settings.mode=mode;WindowStartupLocation=WindowStartupLocation.Manual;Left=-20000;Top=-20000;ShowActivated=false;
   Show();ShowPage(page=="root"?"":System.IO.Path.Combine(foldersDir,page),true);UpdateLayout();if(bottom){scroller.ScrollToBottom();UpdateLayout();}
   Dispatcher.Invoke(new Action(delegate{}),DispatcherPriority.Render);motion.Pause(this);
   Action<string,int> capture=delegate(string path,int ms){
    if(motion!=null)motion.SeekAlignedToLastTick(this,TimeSpan.FromMilliseconds(ms),TimeSeekOrigin.BeginTime);
    Dispatcher.Invoke(new Action(delegate{}),DispatcherPriority.ApplicationIdle);UpdateLayout();
    var bitmap=new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),(int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(root);
    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(path))encoder.Save(stream);
   };
   Directory.CreateDirectory(destination);
   if(frames){
    int[] times={0,35,70,105,140,175,210,245,280,320,380,520};for(int i=0;i<times.Length;i++)capture(System.IO.Path.Combine(destination,"frame-"+i.ToString("00")+".png"),times[i]);
    Dismiss();Dispatcher.Invoke(new Action(delegate{}),DispatcherPriority.Render);motion.Pause(this);int[] closingTimes={0,28,56,84,112,145};for(int i=0;i<closingTimes.Length;i++)capture(System.IO.Path.Combine(destination,"close-"+i.ToString("00")+".png"),closingTimes[i]);
   }
   else capture(System.IO.Path.Combine(destination,page+"-"+mode+(bottom?"-bottom":"")+".png"),650);
   HideImmediately();
  }
  public string SelfTest(){
   captureMode=true;TestMode=true;ShowActivated=false;Left=-20000;Top=-20000;Show();ShowPage("",true);UpdateLayout();FinishMotion();
   Assert(ReadItems("").Select(i=>i.Name).SequenceEqual(new[]{"左边栏","中间栏","右边栏"}),"three spatial groups");
   int count=0;
   foreach(var group in ReadItems("")){
    var entries=ReadItems(group.Path);count+=entries.Count;
    string[] expected; if(settings.entries!=null&&settings.entries.TryGetValue(group.Name,out expected))Assert(entries.Where(i=>!i.Automatic).Select(i=>System.IO.Path.GetFileName(i.Path)).SequenceEqual(expected),"original order: "+group.Name);
    Assert(entries.All(i=>OriginalExists(i.OriginalSource)),"original paths: "+group.Name);
    ShowPage(group.Path,false);FinishMotion();UpdateLayout();
    Assert(Width<=activeWorkArea.Width-16+.5&&Height<=activeWorkArea.Height-16+.5,"screen bounds: "+group.Name);
    if(entries.Count>0){ChooseForTest(0);Assert(LastOpened==entries[0].OriginalSource,"opens original path");}
    Escape();Assert(CurrentPath=="","escape returns home");
   }
   SetTestMode("fan");FinishMotion();Assert(ItemCount==3,"fan mode");SetTestMode("grid");FinishMotion();Assert(ItemCount==3,"grid mode");
   Dismiss();ShowPage("",true);FinishMotion();Assert(!closing&&IsVisible,"interrupt close and reopen");
   foreach(double dpi in new[]{1.0,1.25,1.5}){var area=new Rect(0,0,1920/dpi,1040/dpi);Assert(area.Contains(Place(area,new Point(960/dpi,520/dpi),new Size(BaseWidth,BaseHeight))),"dpi layout");}
   HideImmediately();return json.Serialize(new{groups=3,links=count,pathsValid=true,originalOrder=true,screenBounds=true,fanAndGrid=true,businessActionsExecuted=false});
  }
  void Assert(bool test,string name){if(!test)throw new Exception("验证失败："+name);}
 }
 public static class Program {
  public static bool Quitting;
  public static string StatusFile;
  static void Initialize(string directory){
   string powershell=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","WindowsPowerShell","v1.0","powershell.exe");
   string arguments="-NoLogo -NoProfile -STA -ExecutionPolicy Bypass -File \""+System.IO.Path.Combine(directory,"Initialize.ps1")+"\" -AppDirectory \""+directory+"\"";
   using(var process=Process.Start(new ProcessStartInfo(powershell,arguments){UseShellExecute=false,CreateNoWindow=true})){if(!process.WaitForExit(90000)){try{process.Kill();}catch{}throw new IOException("初始化超时；没有移动或隐藏原文件。");}if(process.ExitCode!=0)throw new IOException("初始化未完成，请在PowerShell运行Initialize.ps1查看原因。");}
  }
  [STAThread] public static int Main(string[] args){
   string directory=AppDomain.CurrentDomain.BaseDirectory;string log=System.IO.Path.Combine(directory,"最近启动错误.txt");
   try{
    if(!File.Exists(System.IO.Path.Combine(directory,"设置.json"))&&!args.Contains("--quit"))Initialize(directory);
    if(args.Length>0&&args[0]=="--quit"){try{using(var quit=EventWaitHandle.OpenExisting("Local\\DesktopStacksWindowQuit"))quit.Set();}catch(WaitHandleCannotBeOpenedException){}return 0;}
    if(args.Length==4&&args[0]=="--auto-configure"){using(var collector=new DesktopAutoCollector(directory,Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),null,TimeSpan.FromSeconds(3)))collector.Configure(bool.Parse(args[2]),args[1],bool.Parse(args[3]));return 0;}
    if(args.Length>1&&args[0]=="--diagnostics")StatusFile=args[1];
    if(args.Length>0&&args[0]=="--self-test"){
     var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var window=new StackWindow(directory,true);string result=window.SelfTest();File.WriteAllText(args.Length>1?args[1]:System.IO.Path.Combine(directory,"自检结果.json"),result,Encoding.UTF8);Quitting=true;window.Close();return 0;
    }
    if(args.Length>1&&args[0]=="--capture"){
     var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var window=new StackWindow(directory,true);window.CaptureView(args[1],args.Length>2?args[2]:"root",args.Length>3?args[3]:"fan",args.Length>4&&args[4]=="frames",args.Length>4&&args[4]=="bottom");Quitting=true;window.Close();return 0;
    }
    bool fresh;using(var mutex=new Mutex(true,"Local\\DesktopStacksWindow",out fresh)){
     string pidFile=System.IO.Path.Combine(directory,"进程.pid");
     if(!fresh){
      if(args.Contains("--background"))return 0;
      try{int pid=int.Parse(File.ReadAllText(pidFile));using(var resident=Process.GetProcessById(pid)){if(string.Equals(resident.MainModule.FileName,Process.GetCurrentProcess().MainModule.FileName,StringComparison.OrdinalIgnoreCase))Native.AllowSetForegroundWindow((uint)pid);}}catch{}
      using(var signal=EventWaitHandle.OpenExisting("Local\\DesktopStacksWindowToggle"))signal.Set();return 0;
     }
     File.WriteAllText(pidFile,Process.GetCurrentProcess().Id.ToString(),Encoding.ASCII);
     using(var signal=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\DesktopStacksWindowToggle"))
     using(var quitSignal=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\DesktopStacksWindowQuit")){
      var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var window=new StackWindow(directory,false);
      var tray=new Forms.NotifyIcon{Icon=new System.Drawing.Icon(System.IO.Path.Combine(directory,"入口图标","办公收纳.ico")),Text="办公收纳 · 左边栏 / 中间栏 / 右边栏",Visible=true};
      Action quit=delegate{Quitting=true;window.StopAutoCollection();tray.Visible=false;tray.Dispose();window.Close();app.Shutdown();};window.ExitRequested=quit;
      tray.MouseClick+=delegate(object sender,Forms.MouseEventArgs e){if(e.Button==Forms.MouseButtons.Left)window.Toggle();else{window.ShowStack();window.menu.IsOpen=true;}};
      var waiting=ThreadPool.RegisterWaitForSingleObject(signal,delegate(object state,bool timeout){app.Dispatcher.BeginInvoke(new Action(window.Toggle));},null,Timeout.Infinite,false);
      var quitWaiting=ThreadPool.RegisterWaitForSingleObject(quitSignal,delegate(object state,bool timeout){app.Dispatcher.BeginInvoke(quit);},null,Timeout.Infinite,false);
      app.Startup+=delegate{window.StartAutoCollection();if(!args.Contains("--background"))window.ShowStack();};app.Exit+=delegate{window.StopAutoCollection();waiting.Unregister(null);quitWaiting.Unregister(null);tray.Dispose();try{if(File.ReadAllText(pidFile)==Process.GetCurrentProcess().Id.ToString())File.Delete(pidFile);}catch{}};app.Run();
     }
    }
    return 0;
   }catch(Exception ex){File.WriteAllText(log,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+"\n"+ex,Encoding.UTF8);if(args.Length==0)MessageBox.Show("收纳入口未能启动。可先打开本目录的收纳文件夹继续使用。\n"+ex.Message,"办公收纳");return 1;}
  }
 }
}
