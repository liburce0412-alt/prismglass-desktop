using System;
internal sealed class ComponentRecovery {
 readonly int[] failures=new int[3];
 readonly DateTime[] retry=new DateTime[3],healthy=new DateTime[3];
 public void Failed(int component,DateTime now){failures[component]++;healthy[component]=DateTime.MinValue;retry[component]=now.AddSeconds(2);}
 public bool CanStart(int component,DateTime now){return failures[component]<3&&now>=retry[component];}
 public bool Exhausted(int component){return failures[component]>=3;}
 public void Healthy(int component,DateTime now){if(healthy[component]==DateTime.MinValue)healthy[component]=now;else if((now-healthy[component]).TotalMinutes>=2)failures[component]=0;}
}
