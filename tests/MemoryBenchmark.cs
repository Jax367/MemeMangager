using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MemeManager;
using D=System.Drawing;

class MemoryBenchmark {
 static object Call(object o,string name){return o.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,null);}
 static T Field<T>(object o,string name){return (T)o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
 static void Pump(int milliseconds){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(milliseconds)};timer.Tick+=(s,e)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
 static void Snapshot(string phase,object manager){var cards=Field<WrapPanel>(manager,"cards");int missing=0;foreach(UIElement element in cards.Children){var border=element as Border;var stack=border==null?null:border.Child as StackPanel;if(stack==null)continue;var grid=stack.Children[0] as Grid;var image=grid.Children[0] as Image;if(image.Source==null)missing++;}using(var p=Process.GetCurrentProcess()){p.Refresh();Console.WriteLine("{0},{1:F1},{2:F1},{3:F1},{4},{5}",phase,p.WorkingSet64/1048576.0,p.PrivateMemorySize64/1048576.0,GC.GetTotalMemory(false)/1048576.0,cards.Children.Count,missing);}}
 [STAThread] static int Main(string[] args){try{
  if(args[0]=="prepare"){Prepare(args[1]);return 0;}
  var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var manager=new ManagerWindow();manager.Initialize(new[]{"--background","--smoke-test","--data-dir",args[1]});
  Console.WriteLine("phase,working_set_MiB,private_bytes_MiB,managed_heap_MiB,visual_children,missing_thumbnails");Pump(900);Snapshot("background_start",manager);
  Call(manager,"Show");Pump(1200);Snapshot("first_view",manager);
  var window=Field<Window>(manager,"window");var scroller=(ScrollViewer)window.FindName("Scroller");
  for(int i=0;i<=24;i++){scroller.ScrollToVerticalOffset(scroller.ScrollableHeight*i/24.0);window.UpdateLayout();Call(manager,"RenderMore");Pump(35);}
  Pump(1200);Snapshot("after_scroll",manager);window.Hide();Pump(3500);Snapshot("hidden_3s",manager);
  Call(manager,"Show");Pump(900);window.Hide();Pump(3500);Snapshot("hidden_again_3s",manager);Call(manager,"Exit");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 static void Prepare(string root){var library=new Library(root);for(int i=0;i<200;i++){string id=i.ToString("D64");using(var bitmap=new D.Bitmap(768,512))using(var graphics=D.Graphics.FromImage(bitmap))using(var gradient=new D.Drawing2D.LinearGradientBrush(new D.Rectangle(0,0,768,512),D.Color.FromArgb(100+i%100,120,160),D.Color.FromArgb(190,190+i%60,220),45))using(var font=new D.Font("Arial",40)){graphics.FillRectangle(gradient,0,0,768,512);graphics.DrawString("MEME "+i,font,D.Brushes.White,60,200);bitmap.Save(Path.Combine(root,"images",id+".png"),D.Imaging.ImageFormat.Png);}library.Data.Memes.Add(new Meme{Id=id,Name="图片 "+i,Extension=".png",Added=DateTime.Now});}
  byte[] gif=Gif(640,480,8);for(int i=200;i<212;i++){string id=i.ToString("D64");File.WriteAllBytes(Path.Combine(root,"images",id+".gif"),gif);library.Data.Memes.Add(new Meme{Id=id,Name="动图 "+i,Extension=".gif",Added=DateTime.Now.AddMinutes(1)});}library.Save();Console.WriteLine("Prepared 212 images (200 PNG 768x512; 12 GIF 640x480, 8 frames).");}
 static byte[] Gif(ushort width,ushort height,int frames){using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){writer.Write(System.Text.Encoding.ASCII.GetBytes("GIF89a"));writer.Write(width);writer.Write(height);writer.Write(new byte[]{128,0,0,40,50,70,180,190,210});
  for(int f=0;f<frames;f++){writer.Write(new byte[]{0x21,0xF9,4,0,10,0,0,0,0x2C,0,0,0,0});writer.Write(width);writer.Write(height);writer.Write((byte)0);writer.Write((byte)2);
   // Fixed three-bit LZW codes, clearing before the dictionary grows to four bits.
   var pixels=new List<byte>();int bits=0,bitCount=0;Action<int> code=c=>{bits|=c<<bitCount;bitCount+=3;while(bitCount>=8){pixels.Add((byte)bits);bits>>=8;bitCount-=8;}};
   for(int p=0;p<width*height;p+=2){code(4);code(f%2);if(p+1<width*height)code(f%2);}code(5);if(bitCount>0)pixels.Add((byte)bits);
   for(int p=0;p<pixels.Count;p+=255){int length=Math.Min(255,pixels.Count-p);writer.Write((byte)length);writer.Write(pixels.GetRange(p,length).ToArray());}writer.Write((byte)0);
  }writer.Write((byte)0x3B);return stream.ToArray();}}
}
