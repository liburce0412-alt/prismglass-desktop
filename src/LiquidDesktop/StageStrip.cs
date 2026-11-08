using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Wpf = System.Windows;
using Input = System.Windows.Input;
using UI = System.Windows.Controls;
using Media = System.Windows.Media;
using M3 = System.Windows.Media.Media3D;

internal sealed partial class StageManager {
 internal sealed class StageStrip:Wpf.Window,IDisposable {
  readonly StageManager owner;readonly string monitor;readonly UI.StackPanel stack=new UI.StackPanel();
  readonly UI.ScrollViewer scroll=new UI.ScrollViewer{VerticalScrollBarVisibility=UI.ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=UI.ScrollBarVisibility.Disabled,CanContentScroll=false,PanningMode=UI.PanningMode.VerticalOnly};
  readonly Dictionary<string,CardView> cards=new Dictionary<string,CardView>();List<Group> groups=new List<Group>();
  readonly System.Diagnostics.Stopwatch clock=System.Diagnostics.Stopwatch.StartNew();
  Rectangle area,placed;int selection;bool paused,rendering,keyboard;double stamp,appearance,scrollGoal,edge;CardView pressed;Wpf.Point down;bool dragging;
  readonly Media.TranslateTransform entrance=new Media.TranslateTransform();
  readonly ComponentPosition position;
  internal bool IsRepositioning {get{return position.Dragging;}}
  readonly List<Tuple<NativeWindowMotion.GenieSurface,Rectangle>> dragSurfaces=new List<Tuple<NativeWindowMotion.GenieSurface,Rectangle>>();
  public bool Motion=true;
  internal int CardCount {get{return cards.Count;}}
  internal double ScrollOffset {get{return scroll.VerticalOffset;}}
  internal double ScrollLimit {get{return scroll.ScrollableHeight;}}
  internal double ViewportHeight {get{return scroll.ViewportHeight;}}
  internal double HoverAngle {get{return cards.Count==0?32:cards.Values.Min(c=>c.angle.Angle);}}
  [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetWindowLongPtr(IntPtr h,int index);
  [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]static extern IntPtr SetWindowLongPtr(IntPtr h,int index,IntPtr value);
  [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int hgt,uint flags);
  IntPtr Handle {get{return new System.Windows.Interop.WindowInteropHelper(this).Handle;}}
  sealed class CardView {
   internal Group group;internal UI.Grid root;internal UI.Viewport3D viewport;internal UI.TextBlock label;
   internal M3.AxisAngleRotation3D angle=new M3.AxisAngleRotation3D(new M3.Vector3D(0,1,0),32);
   internal M3.TranslateTransform3D move=new M3.TranslateTransform3D();internal Media.TranslateTransform drag=new Media.TranslateTransform();
   internal List<Tuple<Member,Media.ImageBrush>> images=new List<Tuple<Member,Media.ImageBrush>>();internal double hover;internal bool over;
  }
  internal StageStrip(StageManager manager,string display){
   owner=manager;monitor=display;Title="PrismGlass 台前调度";WindowStyle=Wpf.WindowStyle.None;ResizeMode=Wpf.ResizeMode.NoResize;AllowsTransparency=true;
   Background=Media.Brushes.Transparent;ShowInTaskbar=false;ShowActivated=false;Topmost=true;Width=242;SizeToContent=Wpf.SizeToContent.Height;
   position=new ComponentPosition(System.IO.Path.GetDirectoryName(owner.journal),"stage",()=>placed,next=>{if(Handle!=IntPtr.Zero&&SetWindowPos(Handle,new IntPtr(-1),next.X,next.Y,next.Width,next.Height,0x10))placed=next;},message=>owner.Error=message);
   var grip=new UI.Border{Height=24,Width=108,CornerRadius=new Wpf.CornerRadius(12),Background=new Media.SolidColorBrush(Media.Color.FromArgb(30,200,220,255)),Cursor=Input.Cursors.SizeAll,HorizontalAlignment=Wpf.HorizontalAlignment.Center,ToolTip="拖动台前调度区域 · 右键恢复默认位置",Child=new UI.TextBlock{Text="⠿  拖动区域",FontSize=11,Foreground=Media.Brushes.White,Opacity=.72,HorizontalAlignment=Wpf.HorizontalAlignment.Center,VerticalAlignment=Wpf.VerticalAlignment.Center}};
   System.Windows.Automation.AutomationProperties.SetName(grip,"移动台前调度区域，右键恢复默认位置");
   grip.MouseLeftButtonDown+=(s,e)=>{if(ComponentPosition.LayoutLocked){e.Handled=true;return;}owner.CancelTransition();position.Begin(System.Windows.Forms.Cursor.Position);e.Handled=true;};
   grip.MouseRightButtonUp+=(s,e)=>{position.Receive("position:reset");e.Handled=true;};
   scroll.Content=stack;var content=new UI.DockPanel();UI.DockPanel.SetDock(grip,UI.Dock.Top);content.Children.Add(grip);content.Children.Add(scroll);Content=content;
   stack.RenderTransform=entrance;IsVisibleChanged+=(s,e)=>{if(IsVisible){appearance=Motion?0:1;Opacity=appearance;entrance.X=-18*(1-appearance);Wake();}};
   System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(this);
   SourceInitialized+=(s,e)=>{SetWindowLongPtr(Handle,-20,new IntPtr(GetWindowLongPtr(Handle,-20).ToInt64()|0x08000080L));};
   PreviewMouseWheel+=(s,e)=>{ScrollWheel(e.Delta);e.Handled=true;};
   scroll.MouseMove+=(s,e)=>HoverScroll(e.GetPosition(scroll));scroll.MouseLeave+=(s,e)=>StopHoverScroll();
   scroll.ScrollChanged+=(s,e)=>{if(e.ExtentHeightChange!=0||e.ViewportHeightChange!=0){scrollGoal=ClampScroll(scrollGoal);Wake();}};
   PreviewKeyDown+=(s,e)=>{if(Key(e.Key)){e.Handled=true;}};
   MouseMove+=Drag;MouseLeftButtonUp+=DropCard;LostMouseCapture+=(s,e)=>{ClearDrag();if(pressed!=null){pressed.drag.X=pressed.drag.Y=0;pressed=null;Invalidate();}dragging=false;};
   SizeChanged+=(s,e)=>Position();
   Closed+=(s,e)=>{position.Dispose();ClearDrag();Rendering(false);};
  }
  internal void SetPaused(bool value){paused=value;if(paused){position.Receive("position:cancel");edge=0;scrollGoal=scroll.VerticalOffset;ReleaseMouseCapture();ClearDrag();Keyboard(false);Rendering(false);Hide();placed=Rectangle.Empty;}else if(groups.Count>0&&!IsVisible){Show();Position();}}
  internal void RefreshGroups(List<Group> value,Rectangle work){
   area=work;groups=value;selection=Math.Max(0,Math.Min(selection,groups.Count-1));var source=System.Windows.PresentationSource.FromVisual(this);double dpi=source==null||source.CompositionTarget==null?1:source.CompositionTarget.TransformToDevice.M22;
   scroll.MaxHeight=Math.Max(1,area.Height/dpi-24);MaxHeight=scroll.MaxHeight+24;Build();if(!paused&&groups.Count>0){if(!IsVisible)Show();Position();}else Hide();
  }
  void Position(){if(Handle==IntPtr.Zero||area.IsEmpty)return;double dpi=System.Windows.PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice.M11;int height=(int)Math.Ceiling(ActualHeight*dpi);var next=new Rectangle(area.Left+8,area.Top+Math.Max(12,(area.Height-height)/2),(int)Math.Ceiling(Width*dpi),height);position.Arrange(next,Screen.FromRectangle(area).WorkingArea,Point.Empty,new Rectangle(Point.Empty,next.Size));}
  internal void Invalidate(){Build();Wake();}
  void Build(){
   var visible=groups;var keys=new HashSet<string>(visible.Select(g=>g.id));
   foreach(var id in cards.Keys.Where(k=>!keys.Contains(k)).ToArray()){stack.Children.Remove(cards[id].root);cards.Remove(id);}
   for(int i=0;i<visible.Count;i++){
    var group=visible[i];CardView card;if(!cards.TryGetValue(group.id,out card)){card=Make(group);cards.Add(group.id,card);}
    if(stack.Children.IndexOf(card.root)!=i){stack.Children.Remove(card.root);stack.Children.Insert(i,card.root);}
    if(!card.images.Select(p=>p.Item1).SequenceEqual(group.members.Take(3).Reverse()))TexturePlanes(card);
    for(int n=0;n<card.images.Count;n++){var pair=card.images[n];System.Windows.Media.Imaging.BitmapSource image;if(owner.pictures.TryGetValue(pair.Item1.window.handle,out image)&&pair.Item2.ImageSource!=image){pair.Item2.ImageSource=image;((M3.GeometryModel3D)((M3.ModelVisual3D)card.viewport.Children[n]).Content).Material=new M3.DiffuseMaterial(pair.Item2);}}
    Group current;bool active=owner.active.TryGetValue(monitor,out current)&&current==group;
    card.root.Opacity=active?.48:1;card.label.Text=group.Name+(group.members.Count>1?"  ·  "+group.members.Count:"");
    card.root.ToolTip=group.Name+"\n点击切换；拖到当前工作区或另一组以合并；右键拆分";
   }
   if(IsVisible)Position();
  }
  CardView Make(Group group){
   var card=new CardView{group=group,root=new UI.Grid{Width=232,Height=136,Background=Media.Brushes.Transparent},viewport=new UI.Viewport3D{Width=220,Height=112,VerticalAlignment=Wpf.VerticalAlignment.Top},label=new UI.TextBlock{Foreground=Media.Brushes.White,FontSize=12,MaxWidth=192,TextTrimming=Wpf.TextTrimming.CharacterEllipsis,HorizontalAlignment=Wpf.HorizontalAlignment.Center,VerticalAlignment=Wpf.VerticalAlignment.Bottom,Margin=new Wpf.Thickness(0,0,0,5),Opacity=.78}};
   card.viewport.Camera=new M3.PerspectiveCamera(new M3.Point3D(0,0,400),new M3.Vector3D(0,0,-1),new M3.Vector3D(0,1,0),32){NearPlaneDistance=1,FarPlaneDistance=1000};
   card.viewport.Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=12,ShadowDepth=4,Opacity=.3,RenderingBias=System.Windows.Media.Effects.RenderingBias.Performance};
   card.root.RenderTransform=card.drag;card.root.Children.Add(card.viewport);card.root.Children.Add(card.label);
   try{using(var icon=(System.IO.File.Exists(group.members[0].window.path)?System.Drawing.Icon.ExtractAssociatedIcon(group.members[0].window.path):null)??(System.Drawing.Icon)SystemIcons.Application.Clone()){var image=System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle,Wpf.Int32Rect.Empty,System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());image.Freeze();card.root.Children.Add(new UI.Image{Source=image,Width=24,Height=24,HorizontalAlignment=Wpf.HorizontalAlignment.Left,VerticalAlignment=Wpf.VerticalAlignment.Top,Margin=new Wpf.Thickness(30,84,0,0),IsHitTestVisible=false});}}catch(ArgumentException){}catch(System.IO.IOException){}catch(System.ComponentModel.Win32Exception){}
   card.root.MouseEnter+=(s,e)=>{card.over=true;owner.Warm(card.group);Wake();};card.root.MouseLeave+=(s,e)=>{card.over=false;Wake();};
   card.root.MouseLeftButtonDown+=(s,e)=>{StopHoverScroll();scrollGoal=scroll.VerticalOffset;selection=groups.IndexOf(card.group);Keyboard(true);pressed=card;down=PointToScreen(e.GetPosition(this));dragging=false;CaptureMouse();e.Handled=true;};
   card.root.MouseRightButtonUp+=(s,e)=>{Keyboard(true);Keyboard(false);Menu(card.group);e.Handled=true;};
   card.label.MouseLeftButtonDown+=(s,e)=>{Keyboard(true);e.Handled=true;};
   System.Windows.Automation.AutomationProperties.SetName(card.root,group.Name);return card;
  }
  void TexturePlanes(CardView card){
   card.viewport.Children.Clear();card.images.Clear();int index=0;
   foreach(var member in card.group.members.Take(3).Reverse()){
    System.Windows.Media.Imaging.BitmapSource image;owner.pictures.TryGetValue(member.window.handle,out image);
    var p=member.layout.normal;double ratio=Math.Max(1,p.r-p.l)/(double)Math.Max(1,p.b-p.t),width=Math.Min(182,94*ratio),height=width/ratio,shift=(card.group.members.Take(3).Count()-1-index++)*7;
    var mesh=RoundedPlane(width,height,shift);
    var brush=new Media.ImageBrush(image){Stretch=Media.Stretch.Fill};card.images.Add(Tuple.Create(member,brush));
    Media.Brush fill=image==null?(Media.Brush)new Media.SolidColorBrush(Media.Color.FromRgb(62,75,106)):brush;
    var material=new M3.DiffuseMaterial(fill);var model=new M3.GeometryModel3D(mesh,material);
    var transforms=new M3.Transform3DGroup();transforms.Children.Add(new M3.RotateTransform3D(card.angle));transforms.Children.Add(card.move);model.Transform=transforms;
    card.viewport.Children.Add(new M3.ModelVisual3D{Content=model});
   }
   card.viewport.Children.Add(new M3.ModelVisual3D{Content=new M3.AmbientLight(Media.Colors.White)});
  }
  static M3.MeshGeometry3D RoundedPlane(double width,double height,double shift){
   double radius=Math.Min(6,Math.Min(width,height)/2);var mesh=new M3.MeshGeometry3D();mesh.Positions.Add(new M3.Point3D(shift,shift,-shift));mesh.TextureCoordinates.Add(new Wpf.Point(.5,.5));
   for(int corner=0;corner<4;corner++)for(int step=0;step<=6;step++){double angle=(corner+step/6.0)*Math.PI/2,cx=corner==0||corner==3?width/2-radius:-width/2+radius,cy=corner<2?height/2-radius:-height/2+radius,x=cx+radius*Math.Cos(angle),y=cy+radius*Math.Sin(angle);mesh.Positions.Add(new M3.Point3D(x+shift,y+shift,-shift));mesh.TextureCoordinates.Add(new Wpf.Point(x/width+.5,.5-y/height));}
   int count=mesh.Positions.Count-1;for(int n=1;n<=count;n++){mesh.TriangleIndices.Add(0);mesh.TriangleIndices.Add(n);mesh.TriangleIndices.Add(n==count?1:n+1);}mesh.Freeze();return mesh;
  }
  internal CardPose Pose(Group group,Member member){
   CardView card;if(!cards.TryGetValue(group.id,out card)||!IsVisible)return new CardPose();
   double top=card.root.TranslatePoint(new Wpf.Point(),scroll).Y;if(top<-.5||top+card.root.ActualHeight>scroll.ViewportHeight+.5)return new CardPose();
   var p=member.layout.normal;double ratio=Math.Max(1,p.r-p.l)/(double)Math.Max(1,p.b-p.t),width=Math.Min(182,94*ratio),height=width/ratio,shift=Math.Min(2,Math.Max(0,group.members.IndexOf(member)))*7;
   double angle=card.angle.Angle*Math.PI/180,z=-shift*(Math.Sin(angle)+Math.Cos(angle)),factor=400/(400-z),scale=card.viewport.ActualWidth/(800*Math.Tan(16*Math.PI/180)),dpi=System.Windows.PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice.M11;
   var center=card.viewport.PointToScreen(new Wpf.Point(card.viewport.ActualWidth/2+(shift*(Math.Cos(angle)-Math.Sin(angle))+card.move.OffsetX)*scale*factor,card.viewport.ActualHeight/2-shift*scale*factor));
   int w=Math.Max(1,(int)Math.Round(width*scale*factor*dpi)),h=Math.Max(1,(int)Math.Round(height*scale*factor*dpi));return new CardPose{bounds=new Rectangle((int)Math.Round(center.X-w/2.0),(int)Math.Round(center.Y-h/2.0),w,h),angle=card.angle.Angle,depth=400*scale*dpi};
  }
  internal Rectangle CardBounds(Group group,Member member){return Pose(group,member).bounds;}
  double ClampScroll(double value){return Math.Max(0,Math.Min(scroll.ScrollableHeight,value));}
  internal void ScrollWheel(int delta){if(paused||pressed!=null||position.Dragging)return;edge=0;scrollGoal=ClampScroll(scrollGoal-delta*.75);Wake();}
  internal void HoverScroll(Wpf.Point pointer){
   double next=0;if(!paused&&pressed==null&&!position.Dragging&&scroll.ScrollableHeight>0&&pointer.X>=0&&pointer.X<=scroll.ActualWidth&&pointer.Y>=0&&pointer.Y<=scroll.ViewportHeight){const double band=36;next=pointer.Y<band?-(1-pointer.Y/band):pointer.Y>scroll.ViewportHeight-band?1-(scroll.ViewportHeight-pointer.Y)/band:0;}
   if(next==0){StopHoverScroll();return;}edge=next;Wake();
  }
  internal void StopHoverScroll(){if(edge!=0){edge=0;scrollGoal=scroll.VerticalOffset;}}
  internal void Reveal(Group group,bool immediately=false){
   int index=groups.IndexOf(group);if(index<0)return;double top=index*136,bottom=top+136,offset=scroll.VerticalOffset;edge=0;
   scrollGoal=ClampScroll(top<offset?top:bottom>offset+scroll.ViewportHeight?bottom-scroll.ViewportHeight:offset);
   if(immediately){scroll.ScrollToVerticalOffset(scrollGoal);UpdateLayout();}else Wake();
  }
  void Rendering(bool value){if(rendering==value)return;rendering=value;if(value)Media.CompositionTarget.Rendering+=Frame;else Media.CompositionTarget.Rendering-=Frame;}
  void Wake(){if(paused||!IsVisible)return;stamp=clock.Elapsed.TotalSeconds;Rendering(true);}
  void Frame(object sender,EventArgs args){
   double now=clock.Elapsed.TotalSeconds,dt=Math.Max(0,Math.Min(.05,now-stamp)),step=Motion?1-Math.Exp(-18*dt):1;stamp=now;bool changing=false;
   if(edge!=0){scrollGoal=ClampScroll(scrollGoal+edge*280*dt);changing=scrollGoal!=scroll.VerticalOffset;}
   double offset=scroll.VerticalOffset+(scrollGoal-scroll.VerticalOffset)*step;if(Math.Abs(scrollGoal-offset)<.1)offset=scrollGoal;else changing=true;
   if(offset!=scroll.VerticalOffset)scroll.ScrollToVerticalOffset(offset);
   appearance+=(1-appearance)*step;if(appearance>.999)appearance=1;else changing=true;Opacity=appearance;entrance.X=-18*(1-appearance);
   foreach(var card in cards.Values){double goal=card.over||keyboard&&groups.IndexOf(card.group)==selection?1:0;card.hover+=(goal-card.hover)*step;if(Math.Abs(goal-card.hover)<.001)card.hover=goal;else changing=true;card.angle.Angle=32-22*card.hover;card.move.OffsetX=12*card.hover;card.label.Opacity=.78+.22*card.hover;}
   if(!changing)Rendering(false);
  }
  void ClearDrag(){foreach(var item in dragSurfaces)item.Item1.Dispose();dragSurfaces.Clear();}
  void Drag(object sender,Input.MouseEventArgs e){
   if(pressed==null)return;var point=PointToScreen(e.GetPosition(this));
   if(!dragging&&(point-down).Length>8){dragging=true;foreach(var member in pressed.group.members.Take(3).Reverse()){System.Windows.Media.Imaging.BitmapSource texture;if(!owner.pictures.TryGetValue(member.window.handle,out texture))continue;var pose=Pose(pressed.group,member);if(pose.bounds.IsEmpty)continue;var surface=new NativeWindowMotion.GenieSurface(texture,area,pose.bounds){StageAngle=pose.angle,StageDepth=pose.depth};surface.RenderStage(1);surface.Show();dragSurfaces.Add(Tuple.Create(surface,pose.bounds));}}
   if(dragging){pressed.root.Opacity=.25;if(dragSurfaces.Count>0)foreach(var item in dragSurfaces){var bounds=item.Item2;bounds.Offset((int)(point.X-down.X),(int)(point.Y-down.Y));item.Item1.MoveCard(bounds);}else{double dpi=System.Windows.PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice.M11;pressed.drag.X=(point.X-down.X)/dpi;pressed.drag.Y=(point.Y-down.Y)/dpi;}}
  }
  void DropCard(object sender,Input.MouseButtonEventArgs e){
   if(pressed==null)return;var card=pressed;bool moved=dragging;var point=PointToScreen(e.GetPosition(this));pressed=null;ReleaseMouseCapture();ClearDrag();card.drag.X=card.drag.Y=0;dragging=false;
   if(moved){Group destination=null;foreach(var other in cards.Values.Where(c=>c!=card)){var bounds=CardBounds(other.group,other.group.members[0]);if(bounds.Contains((int)point.X,(int)point.Y)){destination=other.group;break;}}
    if(destination==null&&point.X>area.Left+Width)owner.active.TryGetValue(monitor,out destination);if(destination!=null)owner.Merge(card.group,destination);else Invalidate();
   }else{Group current;if((Input.Keyboard.Modifiers&Input.ModifierKeys.Shift)!=0&&owner.active.TryGetValue(monitor,out current)&&current!=card.group)owner.Merge(card.group,current);else owner.Switch(card.group,true);}Keyboard(false);
  }
  // ToolStrip finishes dismissing the click after Closed has returned.
  internal ContextMenuStrip Menu(Group group){var menu=DockBackend.CreateMenu();foreach(var target in groups.Where(g=>g!=group)){var destination=target;menu.Items.Add("并入 · "+target.Name,null,(s,e)=>Dispatcher.BeginInvoke((Action)(()=>owner.Merge(group,destination))));}if(group.members.Count>1)foreach(var member in group.members){var picked=member;menu.Items.Add("拆分 · "+member.window.title,null,(s,e)=>Dispatcher.BeginInvoke((Action)(()=>owner.Split(group,picked))));}menu.Closed+=(s,e)=>Dispatcher.BeginInvoke((Action)(()=>menu.Dispose()));menu.Show(System.Windows.Forms.Cursor.Position);return menu;}
  internal void Keyboard(bool value){keyboard=value;if(Handle!=IntPtr.Zero){long style=GetWindowLongPtr(Handle,-20).ToInt64();SetWindowLongPtr(Handle,-20,new IntPtr(value?style&~0x08000000L:style|0x08000000L));}if(value){Activate();Focus();Input.Keyboard.Focus(this);}Wake();}
  internal bool Key(Input.Key key){if(!keyboard)return false;if(key==Input.Key.Escape){Keyboard(false);return true;}if(key==Input.Key.Up||key==Input.Key.Down){selection=Math.Max(0,Math.Min(groups.Count-1,selection+(key==Input.Key.Down?1:-1)));if(groups.Count>0)Reveal(groups[selection]);Wake();return true;}if(key==Input.Key.Enter&&selection<groups.Count){Group current;if((Input.Keyboard.Modifiers&Input.ModifierKeys.Shift)!=0&&owner.active.TryGetValue(monitor,out current))owner.Merge(groups[selection],current);else owner.Switch(groups[selection],true);Keyboard(false);return true;}return false;}
  public void Dispose(){Rendering(false);Close();cards.Clear();}
 }
}
