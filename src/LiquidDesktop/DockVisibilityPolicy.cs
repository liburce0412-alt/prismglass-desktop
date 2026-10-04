using System;
internal sealed class DockVisibilityPolicy {
 DateTime coveredSince=DateTime.MinValue,edgeSince=DateTime.MinValue,revealUntil=DateTime.MinValue;
 public bool Update(DateTime now,bool covered,bool atEdge,bool hovering){
  if(atEdge){if(edgeSince==DateTime.MinValue)edgeSince=now;if((now-edgeSince).TotalMilliseconds>=180)revealUntil=now.AddMilliseconds(900);}else edgeSince=DateTime.MinValue;
  if(hovering)revealUntil=now.AddMilliseconds(900);
  if(!covered){coveredSince=DateTime.MinValue;return true;}
  if(coveredSince==DateTime.MinValue)coveredSince=now;
  return (now-coveredSince).TotalMilliseconds<350||now<revealUntil;
 }
}
