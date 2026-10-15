using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace MemeManager {
 public partial class ManagerWindow {
  Window picker;TextBox pickerSearch;ComboBox pickerGroups;WrapPanel pickerCards;ScrollViewer pickerScroll;TextBlock pickerStatus,pickerEmpty;
  DispatcherTimer pickerSearchTimer;List<Meme> pickerItems=new List<Meme>();bool rebuildingPicker,renderingPicker;int pickerFirst=-1,pickerEnd=-1,pickerColumns=-1;
  class PickerGroup {public string Id,Label;public override string ToString(){return Label;}}

  void TogglePicker(){if(picker!=null&&picker.IsVisible)picker.Hide();else ShowPicker();}
  void ShowPicker(){
   if(picker==null)CreatePicker();releaseTimer.Stop();RefreshPickerGroups();
   picker.Show();if(picker.WindowState==WindowState.Minimized)picker.WindowState=WindowState.Normal;
   picker.UpdateLayout();RefreshPicker();picker.Activate();Native.SetForegroundWindow(new WindowInteropHelper(picker).Handle);pickerSearch.Focus();pickerSearch.SelectAll();
  }
  void CreatePicker(){
   picker=new Window{Title="拾趣 · 选择表情",Icon=window.Icon,Width=604,Height=458,MinWidth=400,MinHeight=300,WindowStartupLocation=WindowStartupLocation.CenterScreen,WindowStyle=WindowStyle.ToolWindow,ResizeMode=ResizeMode.CanResizeWithGrip,ShowInTaskbar=false,Resources=window.Resources,FontFamily=window.FontFamily,FontSize=12,UseLayoutRounding=true};
   picker.SetResourceReference(Window.BackgroundProperty,"Canvas");
   var root=new Grid{Margin=new Thickness(12,10,4,8)};root.SetResourceReference(Grid.BackgroundProperty,"Canvas");root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(29)});
   var tools=new Grid{Margin=new Thickness(0,0,8,10)};tools.ColumnDefinitions.Add(new ColumnDefinition());tools.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(160)});
   var searchGrid=new Grid{Margin=new Thickness(0,0,8,0)};pickerSearch=new TextBox{ToolTip="搜索名称，Enter 复制第一张"};searchGrid.Children.Add(pickerSearch);
   var placeholder=new TextBlock{Text="⌕   搜索表情…",Margin=new Thickness(11,0,0,0),VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};placeholder.SetResourceReference(TextBlock.ForegroundProperty,"Muted");searchGrid.Children.Add(placeholder);tools.Children.Add(searchGrid);
   pickerGroups=new ComboBox();Grid.SetColumn(pickerGroups,1);tools.Children.Add(pickerGroups);root.Children.Add(tools);
   var body=new Grid();Grid.SetRow(body,1);pickerCards=new WrapPanel();pickerScroll=new ScrollViewer{Content=pickerCards,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};body.Children.Add(pickerScroll);
   pickerEmpty=new TextBlock{Text="暂无表情，请到管理窗口导入",HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(12)};pickerEmpty.SetResourceReference(TextBlock.ForegroundProperty,"Muted");body.Children.Add(pickerEmpty);root.Children.Add(body);
   var footer=new Grid{Margin=new Thickness(0,3,8,0)};footer.ColumnDefinitions.Add(new ColumnDefinition());footer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});pickerStatus=new TextBlock{FontSize=10,VerticalAlignment=VerticalAlignment.Center};pickerStatus.SetResourceReference(TextBlock.ForegroundProperty,"Muted");footer.Children.Add(pickerStatus);
   var manage=new Button{Content="表情管理 ↗",Padding=new Thickness(7,3,7,3),FontSize=10};manage.Click+=(s,e)=>{picker.Hide();Show();};Grid.SetColumn(manage,1);footer.Children.Add(manage);Grid.SetRow(footer,2);root.Children.Add(footer);picker.Content=root;
   pickerSearchTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(150)};pickerSearchTimer.Tick+=(s,e)=>{pickerSearchTimer.Stop();if(picker.IsVisible)RefreshPicker();};
   pickerSearch.TextChanged+=(s,e)=>{placeholder.Visibility=pickerSearch.Text.Length==0?Visibility.Visible:Visibility.Collapsed;pickerSearchTimer.Stop();pickerSearchTimer.Start();};
   pickerGroups.SelectionChanged+=(s,e)=>{if(!rebuildingPicker)RefreshPicker();};
   pickerScroll.ScrollChanged+=(s,e)=>{if(e.VerticalChange!=0||e.ViewportHeightChange!=0||e.ViewportWidthChange!=0)RenderPicker();};picker.SizeChanged+=(s,e)=>RenderPicker();
   picker.PreviewKeyDown+=(s,e)=>{if(e.Key==Key.Escape){picker.Hide();e.Handled=true;}else if(e.Key==Key.Enter&&!pickerGroups.IsDropDownOpen&&pickerItems.Count>0){Copy(pickerItems[0],picker);e.Handled=true;}};
   picker.Closing+=(s,e)=>{if(!exiting){e.Cancel=true;picker.Hide();}};
   picker.IsVisibleChanged+=(s,e)=>{if(!picker.IsVisible){pickerSearchTimer.Stop();StopAnimations();pickerCards.Children.Clear();pickerFirst=-1;pickerEnd=-1;if(!window.IsVisible)thumbnails.Clear();releaseTimer.Stop();releaseTimer.Start();}};
  }
  void RefreshPickerGroups(){
   string selected=pickerGroups.SelectedItem==null?"all":((PickerGroup)pickerGroups.SelectedItem).Id;
   rebuildingPicker=true;try{pickerGroups.Items.Clear();pickerGroups.Items.Add(new PickerGroup{Id="all",Label="全部表情"});pickerGroups.Items.Add(new PickerGroup{Id="favorites",Label="我的喜欢"});pickerGroups.Items.Add(new PickerGroup{Id="recent",Label="最近使用"});pickerGroups.Items.Add(new PickerGroup{Id="ungrouped",Label="未分组"});foreach(var group in library.Data.Groups)pickerGroups.Items.Add(new PickerGroup{Id=group.Id,Label=group.Name});pickerGroups.SelectedItem=pickerGroups.Items.Cast<PickerGroup>().FirstOrDefault(x=>x.Id==selected)??pickerGroups.Items[0];}finally{rebuildingPicker=false;}
  }
  void RefreshPicker(){
   if(picker==null)return;RefreshPickerGroups();IEnumerable<Meme> items=library.Data.Memes;
   string group=((PickerGroup)pickerGroups.SelectedItem).Id,query=pickerSearch.Text.Trim();
   if(group=="favorites")items=items.Where(x=>x.Favorite);else if(group=="recent")items=items.Where(x=>x.Uses>0);else if(group=="ungrouped")items=items.Where(x=>x.GroupId==null);else if(group!="all")items=items.Where(x=>x.GroupId==group);
   if(query.Length>0)items=items.Where(x=>x.Name.IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0);
   pickerItems=(group=="recent"?items.OrderByDescending(x=>x.LastUsed).ThenByDescending(x=>x.Added):items.OrderByDescending(x=>x.Uses).ThenByDescending(x=>x.LastUsed).ThenByDescending(x=>x.Added)).ToList();
   pickerEmpty.Text=query.Length>0?"没有匹配的表情":library.Data.Memes.Count==0?"暂无表情，请到管理窗口导入":"这个分组暂无表情";pickerEmpty.Visibility=pickerItems.Count==0?Visibility.Visible:Visibility.Collapsed;
   pickerStatus.Text=pickerItems.Count+" 张  ·  点击复制并收起  ·  Esc 关闭";pickerFirst=-1;pickerEnd=-1;pickerScroll.ScrollToTop();RenderPicker();
  }
  void RenderPicker(){
   if(renderingPicker||picker==null||!picker.IsVisible)return;
   const int tileWidth=112,rowHeight=136;double width=pickerScroll.ViewportWidth;if(width<=0)width=Math.Max(1,picker.ActualWidth-40);
   int columns=Math.Max(1,(int)(width/tileWidth)),rows=(pickerItems.Count+columns-1)/columns;
   int firstRow=Math.Min(rows,Math.Max(0,(int)(pickerScroll.VerticalOffset/rowHeight)-1)),lastRow=Math.Min(rows,(int)Math.Ceiling((pickerScroll.VerticalOffset+Math.Max(1,pickerScroll.ViewportHeight))/rowHeight)+1);
   int first=Math.Min(pickerItems.Count,firstRow*columns),end=Math.Min(pickerItems.Count,lastRow*columns);if(first==pickerFirst&&end==pickerEnd&&columns==pickerColumns)return;
   renderingPicker=true;try{StopAnimations();pickerCards.Children.Clear();pickerFirst=first;pickerEnd=end;pickerColumns=columns;
    if(firstRow>0)pickerCards.Children.Add(new Border{Width=columns*tileWidth,Height=firstRow*rowHeight});
    for(int i=first;i<end;i++)pickerCards.Children.Add(Card(pickerItems[i],true));
    if(lastRow<rows)pickerCards.Children.Add(new Border{Width=columns*tileWidth,Height=(rows-lastRow)*rowHeight});
   }finally{renderingPicker=false;}
  }
 }
}
