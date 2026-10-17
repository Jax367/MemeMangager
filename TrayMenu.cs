using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MemeManager {
 public partial class ManagerWindow {
  ContextMenu CreateTrayMenu(){
   var menu=new ContextMenu{Resources=window.Resources,Style=(Style)window.Resources["CompactMenu"],MinWidth=250,FontFamily=window.FontFamily,FontSize=12};
   var brand=new StackPanel{Orientation=Orientation.Horizontal};brand.Children.Add(new Image{Source=window.Icon,Width=30,Height=30,Margin=new Thickness(0,0,10,0)});var text=new StackPanel();text.Children.Add(new TextBlock{Text="拾趣",FontSize=14,FontWeight=FontWeights.SemiBold});text.Children.Add(new TextBlock{Text="把快乐收好，随时找到",FontSize=10,Foreground=BrushFor("Muted"),Margin=new Thickness(0,2,0,0)});brand.Children.Add(text);
   menu.Items.Add(new MenuItem{Header=brand,Style=(Style)window.Resources["CompactMenuItem"],IsHitTestVisible=false,Focusable=false,Padding=new Thickness(8,8,8,12)});
   Action<string,string,Action,string> item=(label,glyph,action,hint)=>{var header=new Grid();header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});header.Children.Add(new TextBlock{Text=label});if(hint!=null){var shortcut=new TextBlock{Text=hint,FontSize=10,Foreground=BrushFor("Muted"),Margin=new Thickness(20,0,0,0),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(shortcut,1);header.Children.Add(shortcut);}var entry=new MenuItem{Header=header,Icon=new TextBlock{Text=glyph,FontSize=15,Foreground=BrushFor("Accent")},Style=(Style)window.Resources["CompactMenuItem"]};entry.Click+=(s,e)=>{menu.IsOpen=false;action();};menu.Items.Add(entry);};
   item("选择表情","⌕",ShowPicker,HotkeyText(library.Data.Settings.Modifiers,library.Data.Settings.HotKey));item("表情管理","▦",Show,null);item("相似表情查重","⧉",ShowDuplicates,null);item("图片预处理","⌘",ShowPreprocessor,null);item("外观与偏好","⚙",Settings,null);
   menu.Items.Add(new Separator{Template=SeparatorTemplate()});item("退出拾趣","⏻",Exit,null);return menu;
  }
  ControlTemplate SeparatorTemplate(){var border=new FrameworkElementFactory(typeof(Border));border.SetResourceReference(Border.BackgroundProperty,"Line");border.SetValue(Border.HeightProperty,1.0);border.SetValue(Border.MarginProperty,new Thickness(8,6,8,6));return new ControlTemplate(typeof(Separator)){VisualTree=border};}
  void ShowTrayMenu(){var menu=CreateTrayMenu();menu.Placement=PlacementMode.MousePoint;menu.Closed+=(s,e)=>menu.Items.Clear();menu.IsOpen=true;}
 }
}
