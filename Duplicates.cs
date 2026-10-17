using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using D=System.Drawing;

namespace MemeManager {
 public sealed class VisualSignature {
  public Meme Meme;public int Width,Height,Frames=1,Duration;public long Bytes;
  public ulong[] Hashes,Edges;public int[][] Colors;
 }
 public sealed class DuplicateGroup {public List<VisualSignature> Items=new List<VisualSignature>();}
 public sealed class DuplicateResult {public List<DuplicateGroup> Groups=new List<DuplicateGroup>();public int Scanned,Skipped;}
 public static class DuplicateScanner {
  static readonly double[,] Cosines=MakeCosines();
  static double[,] MakeCosines(){var c=new double[8,32];for(int u=0;u<8;u++)for(int x=0;x<32;x++)c[u,x]=Math.Cos((2*x+1)*u*Math.PI/64);return c;}
  public static int Distance(ulong a,ulong b){ulong v=a^b;v=v-((v>>1)&0x5555555555555555UL);v=(v&0x3333333333333333UL)+((v>>2)&0x3333333333333333UL);return (int)((((v+(v>>4))&0x0F0F0F0F0F0F0F0FUL)*0x0101010101010101UL)>>56);}
  static bool UnsupportedAnimation(string file){using(var s=File.OpenRead(file)){var h=new byte[21];int count=s.Read(h,0,h.Length);if(count>=21&&h[0]==82&&h[1]==73&&h[8]==87&&h[9]==69&&h[12]==86&&h[13]==80&&h[14]==56&&h[15]==88)return (h[20]&2)!=0;
    if(count<8||h[0]!=137||h[1]!=80||h[2]!=78||h[3]!=71)return false;s.Position=8;var chunk=new byte[8];while(s.Read(chunk,0,8)==8){uint length=((uint)chunk[0]<<24)|((uint)chunk[1]<<16)|((uint)chunk[2]<<8)|chunk[3];string type=System.Text.Encoding.ASCII.GetString(chunk,4,4);if(type=="acTL")return true;if(type=="IDAT"||type=="IEND")return false;if(length>s.Length-s.Position-4)return false;s.Position+=length+4;}return false;}}
  public static VisualSignature Read(string file,Meme meme,CancellationToken token){try{return ReadDrawing(file,meme,token);}catch(ArgumentException){return ReadWic(file,meme,token);}catch(OutOfMemoryException){return ReadWic(file,meme,token);}}
  static VisualSignature ReadWic(string file,Meme meme,CancellationToken token){
   token.ThrowIfCancellationRequested();if(UnsupportedAnimation(file))throw new NotSupportedException();
   using(var input=File.OpenRead(file)){var decoder=System.Windows.Media.Imaging.BitmapDecoder.Create(input,System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,System.Windows.Media.Imaging.BitmapCacheOption.OnDemand);int width=decoder.Frames[0].PixelWidth,height=decoder.Frames[0].PixelHeight;if((long)width*height>100000000)throw new IOException("图片尺寸过大。");input.Position=0;var image=new System.Windows.Media.Imaging.BitmapImage();image.BeginInit();image.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;image.DecodePixelWidth=32;image.DecodePixelHeight=32;image.StreamSource=input;image.EndInit();image.Freeze();using(var png=new MemoryStream()){var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));encoder.Save(png);png.Position=0;using(var decoded=D.Image.FromStream(png))using(var small=new D.Bitmap(32,32))using(var g=D.Graphics.FromImage(small)){g.Clear(D.Color.White);g.DrawImage(decoded,0,0,32,32);ulong hash,edge;int[] color;Analyze(small,out hash,out edge,out color);return new VisualSignature{Meme=meme,Width=width,Height=height,Bytes=new FileInfo(file).Length,Hashes=new[]{hash},Edges=new[]{edge},Colors=new[]{color}};}}}
  }
  static VisualSignature ReadDrawing(string file,Meme meme,CancellationToken token){
   token.ThrowIfCancellationRequested();if(UnsupportedAnimation(file))throw new NotSupportedException("动态 PNG/WebP 不参与近似查重，保护未完整比较的动画。");
   var signature=new VisualSignature{Meme=meme,Bytes=new FileInfo(file).Length};
   using(var original=D.Image.FromFile(file)){
    signature.Width=original.Width;signature.Height=original.Height;if((long)original.Width*original.Height>100000000)throw new IOException("图片尺寸过大。");if(original.FrameDimensionsList.Contains(D.Imaging.FrameDimension.Page.Guid)&&original.GetFrameCount(D.Imaging.FrameDimension.Page)>1)throw new NotSupportedException("多页图片不参与近似查重。");
    bool gif=original.RawFormat.Guid==D.Imaging.ImageFormat.Gif.Guid;if(gif){signature.Frames=original.GetFrameCount(D.Imaging.FrameDimension.Time);if(signature.Frames>2000)throw new NotSupportedException("动画帧数过多。");if(signature.Frames>1)try{var delay=original.GetPropertyItem(0x5100).Value;for(int i=0;i<signature.Frames;i++)signature.Duration+=i*4+4<=delay.Length?Math.Max(20,Math.Min(60000,BitConverter.ToInt32(delay,i*4)*10)):100;}catch{signature.Duration=signature.Frames*100;}}
    int[] frames=signature.Frames>1?new[]{0,signature.Frames/2,signature.Frames-1}:new[]{0};signature.Hashes=new ulong[frames.Length];signature.Edges=new ulong[frames.Length];signature.Colors=new int[frames.Length][];
    for(int index=0;index<frames.Length;index++){token.ThrowIfCancellationRequested();if(signature.Frames>1)original.SelectActiveFrame(D.Imaging.FrameDimension.Time,frames[index]);using(var small=new D.Bitmap(32,32))using(var g=D.Graphics.FromImage(small)){g.Clear(D.Color.White);g.InterpolationMode=D.Drawing2D.InterpolationMode.HighQualityBicubic;g.DrawImage(original,0,0,32,32);Analyze(small,out signature.Hashes[index],out signature.Edges[index],out signature.Colors[index]);}}
   }return signature;
  }
  static void Analyze(D.Bitmap image,out ulong hash,out ulong edge,out int[] color){
   var light=new double[32,32];int red=0,green=0,blue=0;for(int y=0;y<32;y++)for(int x=0;x<32;x++){var p=image.GetPixel(x,y);red+=p.R;green+=p.G;blue+=p.B;light[y,x]=.299*p.R+.587*p.G+.114*p.B;}color=new[]{red/1024,green/1024,blue/1024};
   var horizontal=new double[32,8];for(int y=0;y<32;y++)for(int u=0;u<8;u++)for(int x=0;x<32;x++)horizontal[y,u]+=light[y,x]*Cosines[u,x];
   var coefficients=new double[64];for(int v=0;v<8;v++)for(int u=0;u<8;u++){double sum=0;for(int y=0;y<32;y++)sum+=horizontal[y,u]*Cosines[v,y];sum*=(u==0?1/Math.Sqrt(2):1)*(v==0?1/Math.Sqrt(2):1);coefficients[v*8+u]=Math.Abs(sum)<.000001?0:sum;}
   var ordered=coefficients.Skip(1).OrderBy(x=>x).ToArray();double median=ordered[31];hash=0;for(int i=1;i<64;i++)if(coefficients[i]>median)hash|=1UL<<i;
   edge=0;for(int y=0;y<8;y++)for(int x=0;x<8;x++)if(light[y*31/7,x*31/8]>light[y*31/7,(x+1)*31/8])edge|=1UL<<(y*8+x);
  }
  public static bool Similar(VisualSignature a,VisualSignature b,int threshold){
   if(a.Frames!=b.Frames||a.Hashes.Length!=b.Hashes.Length)return false;
   double ratioA=(double)a.Width/a.Height,ratioB=(double)b.Width/b.Height;if(Math.Abs(ratioA-ratioB)/Math.Max(ratioA,ratioB)>.04)return false;
   if(a.Frames>1&&Math.Abs((double)a.Duration-b.Duration)>Math.Max(30,Math.Max(a.Duration,b.Duration)*.05))return false;
   for(int i=0;i<a.Hashes.Length;i++){if(Distance(a.Hashes[i],b.Hashes[i])>threshold||Distance(a.Edges[i],b.Edges[i])>threshold+2)return false;for(int c=0;c<3;c++)if(Math.Abs(a.Colors[i][c]-b.Colors[i][c])>18)return false;}return true;
  }
  sealed class Node {public VisualSignature Value;public Dictionary<int,Node> Children=new Dictionary<int,Node>();}
  static void Add(ref Node root,VisualSignature value){if(root==null){root=new Node{Value=value};return;}var node=root;while(true){int distance=Distance(value.Hashes[0],node.Value.Hashes[0]);Node child;if(!node.Children.TryGetValue(distance,out child)){node.Children.Add(distance,new Node{Value=value});return;}node=child;}}
  static IEnumerable<VisualSignature> Neighbors(Node root,ulong hash,int threshold,CancellationToken token){var stack=new Stack<Node>();if(root!=null)stack.Push(root);while(stack.Count>0){token.ThrowIfCancellationRequested();var node=stack.Pop();int d=Distance(hash,node.Value.Hashes[0]);if(d<=threshold)yield return node.Value;foreach(var child in node.Children)if(child.Key>=d-threshold&&child.Key<=d+threshold)stack.Push(child.Value);}}
  public static DuplicateResult Scan(string root,IEnumerable<Meme> memes,int threshold,CancellationToken token,Action<int,int> progress){
   if(threshold<0||threshold>12)throw new ArgumentOutOfRangeException("threshold");var result=new DuplicateResult();var inputs=memes.ToList();var signatures=new List<VisualSignature>();Node tree=null;
   foreach(var meme in inputs){token.ThrowIfCancellationRequested();try{var value=Read(Path.Combine(root,"images",meme.Id+meme.Extension),meme,token);signatures.Add(value);Add(ref tree,value);}catch(OperationCanceledException){throw;}catch{result.Skipped++;}result.Scanned++;if(result.Scanned%10==0||result.Scanned==inputs.Count)progress(result.Scanned,inputs.Count);}
   var used=new HashSet<string>();foreach(var anchor in signatures.OrderByDescending(x=>x.Meme.Uses).ThenByDescending(x=>(long)x.Width*x.Height).ThenBy(x=>x.Meme.Added).ThenBy(x=>x.Meme.Id,StringComparer.Ordinal)){
    token.ThrowIfCancellationRequested();if(used.Contains(anchor.Meme.Id))continue;var matches=Neighbors(tree,anchor.Hashes[0],threshold,token).Where(x=>x.Meme.Id!=anchor.Meme.Id&&!used.Contains(x.Meme.Id)&&Similar(anchor,x,threshold)).OrderBy(x=>x.Meme.Name).ToList();if(matches.Count==0)continue;
    var group=new DuplicateGroup();group.Items.Add(anchor);group.Items.AddRange(matches);foreach(var item in group.Items)used.Add(item.Meme.Id);result.Groups.Add(group);
   }return result;
  }
 }
 public sealed class DuplicateTrashManifest {public List<Meme> Memes {get;set;} public List<Group> Groups {get;set;} }
 public static class DuplicateTrash {
  public static string Latest(Library library){string root=Path.Combine(library.Root,"duplicate-trash");if(!Directory.Exists(root))return null;return Directory.GetDirectories(root).OrderByDescending(x=>x,StringComparer.Ordinal).FirstOrDefault(x=>File.Exists(Path.Combine(x,"manifest.json"))&&Directory.EnumerateFiles(x).Any(f=>Path.GetFileName(f)!="manifest.json"));}
  public static string Remove(Library library,IEnumerable<string> ids){
   var selected=new HashSet<string>(ids);var before=library.Data.Memes.ToList();var removed=before.Where(x=>selected.Contains(x.Id)).ToList();if(removed.Count==0)return null;
   string folder=Path.Combine(library.Root,"duplicate-trash",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));Directory.CreateDirectory(folder);var moved=new List<Meme>();
   try{File.WriteAllText(Path.Combine(folder,"manifest.json"),new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Serialize(new DuplicateTrashManifest{Memes=removed,Groups=library.Data.Groups.ToList()}));
    foreach(var item in removed){string source=library.FileFor(item);RequireImagePath(library,source);if(File.Exists(source)){File.Move(source,Path.Combine(folder,Path.GetFileName(source)));moved.Add(item);}}
    library.Data.Memes.RemoveAll(x=>selected.Contains(x.Id));library.Save();return folder;
   }catch{library.Data.Memes=before;foreach(var item in moved){string source=Path.Combine(folder,Path.GetFileName(library.FileFor(item)));if(File.Exists(source))File.Move(source,library.FileFor(item));}throw;}
  }
  static void RequireImagePath(Library library,string file){if(!Path.GetFullPath(file).StartsWith(Path.GetFullPath(Path.Combine(library.Root,"images"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("无效的收藏文件路径。");}
  public static int Restore(Library library,string folder){
   string prefix=Path.GetFullPath(Path.Combine(library.Root,"duplicate-trash"))+Path.DirectorySeparatorChar;if(!Path.GetFullPath(folder).StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new IOException("无效的回收区路径。");
   var manifest=new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Deserialize<DuplicateTrashManifest>(File.ReadAllText(Path.Combine(folder,"manifest.json")));var before=library.Data.Memes.ToList();var groups=library.Data.Groups.ToList();var moved=new List<Meme>();
   try{foreach(var item in manifest.Memes){bool exists=library.Data.Memes.Any(x=>x.Id==item.Id);string target=library.FileFor(item);RequireImagePath(library,target);if(exists&&File.Exists(target))continue;if(File.Exists(target))throw new IOException("恢复目标已存在，请先检查资料库。");File.Move(Path.Combine(folder,Path.GetFileName(target)),target);moved.Add(item);if(!exists)library.Data.Memes.Add(item);if(item.GroupId!=null&&!library.Data.Groups.Any(x=>x.Id==item.GroupId)){var old=manifest.Groups.FirstOrDefault(x=>x.Id==item.GroupId);if(old!=null)library.Data.Groups.Add(old);else item.GroupId=null;}}library.Save();return moved.Count;
   }catch{library.Data.Memes=before;library.Data.Groups=groups;foreach(var item in moved)File.Move(library.FileFor(item),Path.Combine(folder,Path.GetFileName(library.FileFor(item))));throw;}
  }
 }
}
