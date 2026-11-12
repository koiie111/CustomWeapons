using System.Reflection;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
public class CWReviewProbe:BasePlugin {
 public override string ModuleName=>"CWReviewProbe";public override string ModuleVersion=>"1.0.0";
 public override void Load(bool hotReload){AddCommand("css_cwqa_run","Native decoration lifecycle workload",(p,c)=>{
 if(p!=null||Utilities.GetPlayers().Any(p=>!p.IsBot)){c.ReplyToCommand("REJECTED occupied");return;}
 var asm=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="CustomWeapons");var type=asm.GetType("CustomWeapons.CustomWeaponsPlugin")!;
 var plugins=typeof(BasePlugin).Assembly.GetType("CounterStrikeSharp.API.Core.PluginContext");
 // The test owns a separate plugin instance solely for its per-entity caches;
 // natives and schemas are the real server API, without fake clients or grants.
 var instance=Activator.CreateInstance(type)!;var config=type.GetProperty("Config")!.GetValue(instance)!;
 config.GetType().GetProperty("WebsiteSelectionsEnabled")!.SetValue(config,true);
 var hide=type.GetMethod("HideCustomDecorations",BindingFlags.Instance|BindingFlags.NonPublic)!;
 var state=asm.GetType("CustomWeapons.WeaponState")!;var restore=state.GetMethod("RestoreDecorations",BindingFlags.NonPublic|BindingFlags.Static)!;
 var dict=(System.Collections.IDictionary)type.GetField("_decorations",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(instance)!;
 var bot=Utilities.GetPlayers().Single(p=>p.IsBot && p.PawnIsAlive);
 var write=new MemoryFunctionVoid<nint,string,float>(GameData.GetSignature("CW_SetOrAddAttributeValueByName"));
 int failures=0;
 for(int n=0;n<50;n++){
 CCSWeaponBase? w=null;
 try {w=bot.GiveNamedItem<CCSWeaponBase>("weapon_ak47");var item=w!.AttributeManager.Item;item.AttributeList.Attributes.RemoveAll();item.NetworkedDynamicAttributes.Attributes.RemoveAll();
 item.ItemID=100000UL+(ulong)n*3;
 write.Invoke(item.AttributeList.Handle,"keychain slot 0 id",BitConverter.Int32BitsToSingle(37));
 hide.Invoke(instance,new object[]{w});
 if(item.AttributeList.Attributes.Count!=0)throw new Exception("initial hide");
 item.ItemID++;item.AttributeList.Attributes.RemoveAll();
 write.Invoke(item.AttributeList.Handle,"keychain slot 0 id",BitConverter.Int32BitsToSingle(12));
 hide.Invoke(instance,new object[]{w});
 var saved=dict[w.EntityHandle.Raw]!;restore.Invoke(null,new object[]{w,saved});
 if(item.AttributeList.Attributes.Count!=1)throw new Exception("restore count");
 var read=asm.GetType("CustomWeapons.EconAttributeVector")!.GetMethod("Read",BindingFlags.Public|BindingFlags.Static)!;
 var values=(CEconItemAttribute[])read.Invoke(null,new object[]{item.AttributeList})!;
 if(BitConverter.SingleToInt32Bits(values.Single().Value)!=12)throw new Exception("stale restored charm");
 hide.Invoke(instance,new object[]{w});item.ItemID++;item.AttributeList.Attributes.RemoveAll();
 hide.Invoke(instance,new object[]{w});if(dict.Contains(w.EntityHandle.Raw))throw new Exception("removed accessory resurrected");
 }catch(Exception ex){failures++;Console.WriteLine("CWQA "+ex.GetBaseException().Message);}
 finally {if(w is {IsValid:true}) {bot.PlayerPawn.Value?.ItemServices?.As<CCSPlayer_ItemServices>().DropActivePlayerWeapon(w);w.Remove();}}
 }
 c.ReplyToCommand("CWQA items=50 failures="+failures);
 });}
}
