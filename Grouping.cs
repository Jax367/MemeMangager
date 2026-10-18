using System;
using System.Collections.Generic;
using System.Linq;

namespace MemeManager {
 public sealed class GroupRow {public Group Group;public int Depth;public string Path;}
 public static class Grouping {
  public static bool Has(Meme meme,string id){return id!=null&&meme.GroupIds.Contains(id);}
  public static bool IsGrouped(Meme meme){return meme.GroupIds.Count>0;}
  public static void Set(Meme meme,IEnumerable<string> ids){meme.GroupIds=ids.Where(x=>!string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal).ToList();}
  public static void Add(Meme meme,string id){if(id!=null&&!meme.GroupIds.Contains(id))Set(meme,meme.GroupIds.Concat(new[]{id}));}
  public static void Remove(Meme meme,string id){Set(meme,meme.GroupIds.Where(x=>x!=id));}
  public static HashSet<string> Branch(IEnumerable<Group> groups,string id){var result=new HashSet<string>();if(id==null)return result;result.Add(id);var children=groups.GroupBy(x=>x.ParentId??"").ToDictionary(x=>x.Key,x=>x.ToList());var queue=new Queue<string>();queue.Enqueue(id);while(queue.Count>0){List<Group> list;if(children.TryGetValue(queue.Dequeue(),out list))foreach(var group in list)if(result.Add(group.Id))queue.Enqueue(group.Id);}return result;}
  public static bool Matches(Meme meme,HashSet<string> branch){return meme.GroupIds.Any(branch.Contains);}
  public static string Path(IEnumerable<Group> groups,string id){var map=groups.GroupBy(x=>x.Id).ToDictionary(x=>x.Key,x=>x.First());var names=new List<string>();var seen=new HashSet<string>();Group group;while(id!=null&&seen.Add(id)&&map.TryGetValue(id,out group)){names.Add(group.Name);id=group.ParentId;}names.Reverse();return string.Join(" / ",names);}
  public static IEnumerable<GroupRow> Rows(IEnumerable<Group> groups){var all=groups.ToList();var children=all.GroupBy(x=>x.ParentId??"").ToDictionary(x=>x.Key,x=>x.ToList());var stack=new Stack<GroupRow>();List<Group> roots;if(children.TryGetValue("",out roots))for(int i=roots.Count-1;i>=0;i--)stack.Push(new GroupRow{Group=roots[i],Depth=0,Path=roots[i].Name});var seen=new HashSet<string>();while(stack.Count>0){var row=stack.Pop();if(!seen.Add(row.Group.Id))continue;yield return row;List<Group> nested;if(children.TryGetValue(row.Group.Id,out nested))for(int i=nested.Count-1;i>=0;i--)stack.Push(new GroupRow{Group=nested[i],Depth=row.Depth+1,Path=row.Path+" / "+nested[i].Name});}foreach(var orphan in all)if(seen.Add(orphan.Id))yield return new GroupRow{Group=orphan,Path=orphan.Name};}
  public static void Normalize(Database db){var ids=new HashSet<string>(db.Groups.Select(x=>x.Id));foreach(var group in db.Groups){if(group.ParentId!=null&&!ids.Contains(group.ParentId))group.ParentId=null;var seen=new HashSet<string>{group.Id};string parent=group.ParentId;while(parent!=null){if(!seen.Add(parent)){group.ParentId=null;break;}var current=db.Groups.FirstOrDefault(x=>x.Id==parent);parent=current==null?null:current.ParentId;}}foreach(var meme in db.Memes)Set(meme,meme.GroupIds.Where(ids.Contains));}
  public static void SetParent(IEnumerable<Group> groups,Group group,string parent){if(parent!=null&&(!groups.Any(x=>x.Id==parent)||Branch(groups,group.Id).Contains(parent)))throw new InvalidOperationException("不能将分组放入自身或自己的子分组。");group.ParentId=parent;}
  public static void DeleteNode(Database db,Group group){foreach(var item in db.Memes)Remove(item,group.Id);foreach(var child in db.Groups.Where(x=>x.ParentId==group.Id))child.ParentId=group.ParentId;db.Groups.Remove(group);}
 }
}
