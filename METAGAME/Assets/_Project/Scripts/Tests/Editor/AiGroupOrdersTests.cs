using System.Collections;
using System.Linq;
using Metagame.Network;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    public sealed class AiGroupOrdersTests
    {
        sealed class Runtime : IAiRuntimeStatus { public AiRuntimeState State => AiRuntimeState.Ready; }

        /// <summary>
        /// 거절 사유가 <b>보고 문자열에 실제로 실린다</b> (S15P21B202-715).
        ///
        /// <para>전에 남던 것은 <c>0/1명</c> 뿐이었다. 사유를 만들어 놓고 화면까지
        /// 안 이으면 아무것도 달라지지 않으므로, 글자로 못박는다.</para>
        /// </summary>
        [Test] public void RefusalsAreNamedAndGrouped()
        {
            Assert.That(AiRefusalTally.Summarize(new string[0]), Is.Empty);
            Assert.That(AiRefusalTally.Summarize(new[] { "기절해 있음" }),
                        Is.EqualTo(" · 거절 1: 기절해 있음"));
            Assert.That(AiRefusalTally.Summarize(new[] { "기절해 있음", "기절해 있음", "설 자리를 못 찾음" }),
                        Is.EqualTo(" · 거절 3: 기절해 있음 ×2, 설 자리를 못 찾음"));
        }

        /// <summary>사유 없는 거절은 <b>사유 없음이라고 적는다</b> — 조용히 사라지지 않는다.</summary>
        [Test] public void MissingReasonStillCounts()
        {
            var tally = new AiRefusalTally();
            tally.Add(null);
            Assert.That(tally.Summary(), Is.EqualTo(" · 거절 1: 알 수 없음"));
        }

        [Category("Heavy")]   // 느려서 MR 에서 뺀다 (S15P21B202-731)
        [UnityTest]
        public IEnumerator HostAssignsCountsProtectsBusyActorsAndSharesSocialSignals()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            TeamSlots.Clear(); AiHostSettings.BeginRoom();
            var lobby = AiHostPolicyTests.CreateLobby();
            Assert.That(AiHostSettings.TrySetRequested(true), Is.True);
            AiHostSettings.BindRuntime(new Runtime());
            var floor = new GameObject("test floor");
            Assert.That(floor,Is.Not.Null,"Floor object");
            Assert.That(floor.transform,Is.Not.Null,"Floor transform");
            floor.AddComponent<BoxCollider>();
            floor.transform.position = new Vector3(0,-.5f,0);
            floor.transform.localScale = new Vector3(50,1,50);
            var host = PlayerRig.Create("Host", Vector3.up * .1f, Quaternion.identity, Color.red, Color.white, false);
            var session = new GameObject("session").AddComponent<GameSession>(); session.Begin(host,null,Vector3.zero); session.ScriptedInput=true;
            var root = new GameObject("network"); root.SetActive(false);
            var transport = root.AddComponent<TelepathyTransport>(); transport.port=27966;
            var manager = root.AddComponent<NetworkManager>(); manager.transport=transport;
            manager.playerPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Network/PF_NetPlayer.prefab");
            manager.headlessStartMode=HeadlessStartOptions.DoNothing; manager.onlineScene=""; manager.offlineScene="";
            root.SetActive(true); manager.StartHost();
            float until=Time.realtimeSinceStartup+20;
            while(NetPlayer.AllServerPlayers.Count(p=>p!=null && p.IsAi && p.HasBody)!=3 && Time.realtimeSinceStartup<until) yield return null;
            var ais=NetPlayer.AllServerPlayers.Where(p=>p!=null && p.IsAi && p.HasBody).OrderBy(p=>p.teamSlot).ToArray();
            Assert.That(ais.Length,Is.EqualTo(3));
            // Batch-mode server bodies intentionally omit render models. Attach real
            // suits in this fixture so dance playback is exercised, not skipped.
            foreach(var ai in ais)
            {
                var doll=ai.VisualRig.Doll;
                if(doll.Suit==null) doll.Suit=SpaceSuit.Attach(doll,doll.Hips.transform.parent);
                Assert.That(doll.Suit,Is.Not.Null);
                doll.Suit.InitializeLocomotion();
            }
            yield return new WaitForSeconds(1);
            var world=session.GetComponent<AiNavigationWorld>();
            until=Time.realtimeSinceStartup+20;
            while(!world.Ready && Time.realtimeSinceStartup<until) yield return null;
            Assert.That(world.Ready,Is.True);
            int idleBuilds = world.BuildCount;
            yield return new WaitForSeconds(2.1f);
            Assert.That(world.BuildCount, Is.EqualTo(idleBuilds), "Idle AI must retain the warm mesh without periodic rebuilds");
            Assert.That(AiGroupOrders.Capture(AiRecipients.All,null,host).Count,Is.EqualTo(3));
            // Hearing membership is fixed at V-down, not after speech recognition.
            var savedPositions = ais.Select(a => a.VisualRig.FootPosition).ToArray();
            ais[0].VisualRig.Teleport(host.FootPosition + Vector3.right * 2, Quaternion.identity);
            ais[1].VisualRig.Teleport(host.FootPosition + Vector3.right * 25, Quaternion.identity);
            ais[2].VisualRig.Teleport(host.FootPosition + Vector3.up * 25, Quaternion.identity);
            var hearing = AiGroupOrders.Capture(AiRecipients.All, null, host, VoiceRules.Range);
            Assert.That(hearing.Count, Is.EqualTo(1));
            Assert.That(hearing.Nearest, Is.EqualTo(ais[0]));
            ais[0].VisualRig.Teleport(host.FootPosition + Vector3.right * 30, Quaternion.identity);
            ais[1].VisualRig.Teleport(host.FootPosition + Vector3.right * 1, Quaternion.identity);
            Assert.That(hearing.Select("all", AiRecipients.All, null).Count, Is.EqualTo(1));
            Assert.That(hearing.Nearest, Is.EqualTo(ais[0]), "Later arrivals must not join the recorded command");
            for (int i = 0; i < ais.Length; i++) ais[i].VisualRig.Teleport(savedPositions[i], Quaternion.identity);
            Assert.That(AiGroupOrders.Capture(AiRecipients.TwoIdle,null,host).Execute("follow",host),Does.Contain("2/2명"));
            Assert.That(ais.Count(a=>a.AiBusy),Is.EqualTo(2));
            var remaining=AiGroupOrders.Capture(AiRecipients.TwoIdle,null,host);
            Assert.That(remaining.Count,Is.EqualTo(1));
            Assert.That(remaining.Execute("greet",host),Does.Contain("1/2명"));
            Assert.That(AiGroupOrders.Capture(AiRecipients.OneIdle,null,host).Count,Is.Zero);
            until=Time.realtimeSinceStartup+3;
            while(!ais.Any(a=>a.AiGreetingActive) && Time.realtimeSinceStartup<until) yield return null;
            Assert.That(ais.Count(a=>a.AiGreetingActive),Is.EqualTo(1),string.Join(" | ",ais.Select(a=>a.AiActivity+" facing="+a.VisualRig.FacingDirection+" ragdoll="+a.VisualRig.Ragdolled)));
            Assert.That(ais.Count(a=>a.EmoteId==3),Is.Zero,"Greeting is a wave, not a thumbs-up");
            Assert.That(AiGroupOrders.Capture(AiRecipients.All,null,host).Execute("dance",host),Does.Contain("0/3명"),"Social commands must not interrupt tasks");
            AiGroupOrders.Capture(AiRecipients.All,null,host).Execute("cancel",host);
            Assert.That(ais.All(a=>!a.AiBusy),Is.True);
            var stale=AiGroupOrders.Capture(AiRecipients.All,null,host);
            Assert.That(ais[0].TryApplyAiOrder("follow",host),Is.True);
            Assert.That(stale.Execute("heart",host),Does.Contain("2/3명"),"Older reply must not replace newer order");
            Assert.That(ais[0].AiActivity,Is.EqualTo("따라오기"));
            AiGroupOrders.Capture(AiRecipients.All,null,host).Execute("cancel",host);
            foreach(var ai in ais) Assert.That(ai.VisualRig.Doll.Suit.Locomotion.HasDance(1),Is.True);
            Assert.That(AiGroupOrders.Capture(AiRecipients.All,null,host).Execute("dance",host),Does.Contain("3/3명"));
            until=Time.realtimeSinceStartup+4;
            while(!ais.All(a=>a.VisualRig.DanceHolding) && Time.realtimeSinceStartup<until) yield return null;
            Assert.That(ais.All(a=>a.danceId==1 && a.danceSeq>0 && a.VisualRig.DanceHolding),Is.True,string.Join(" | ",ais.Select(a=>a.AiActivity+" facing="+a.VisualRig.FacingDirection+" ragdoll="+a.VisualRig.Ragdolled)));
            uint sequence=ais[0].danceSeq;
            AiGroupOrders.Capture(AiRecipients.All,null,host).Execute("cancel",host);
            Assert.That(ais.All(a=>!a.VisualRig.DanceHolding && a.danceId==0),Is.True);
            Assert.That(ais[0].danceSeq,Is.GreaterThan(sequence),"Cancellation must also be replicated");
            for(int i=0;i<ais.Length;i++) ais[i].VisualRig.Teleport(new Vector3((i-1)*4,.1f,9),Quaternion.identity);
            yield return new WaitForSeconds(1);
            until=Time.realtimeSinceStartup+20;
            while(!world.Ready && Time.realtimeSinceStartup<until) yield return null;
            Assert.That(AiGroupOrders.Capture(AiRecipients.All,null,host).Execute("gather",host),Does.Contain("3/3명"));
            until=Time.realtimeSinceStartup+35;
            while(ais.Any(a=>a.AiBusy) && Time.realtimeSinceStartup<until) yield return null;
            Assert.That(ais.All(a=>!a.AiBusy),Is.True,string.Join(" | ",ais.Select(a=>a.AiActivity)));
            foreach(var ai in ais) Assert.That(Vector3.Distance(ai.VisualRig.FootPosition,host.FootPosition),Is.LessThan(4));
            for(int i=0;i<ais.Length;i++) for(int j=i+1;j<ais.Length;j++)
                Assert.That(Vector3.Distance(ais[i].VisualRig.FootPosition,ais[j].VisualRig.FootPosition),Is.GreaterThan(1));
            until=Time.realtimeSinceStartup+4;
            while(!AllFacing(ais,host) && Time.realtimeSinceStartup<until) yield return null;
            foreach(var ai in ais)
            {
                var direction=host.FootPosition-ai.VisualRig.FootPosition; direction.y=0;
                Assert.That(Vector3.Angle(ai.VisualRig.FacingDirection,direction),Is.LessThan(15),ai.nickname+" "+ai.AiActivity+" yaw="+ai.VisualRig.CurrentInput.AimYaw+" want="+Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg+" rag="+ai.VisualRig.Ragdolled+" dance="+ai.VisualRig.DanceHolding+" facing="+ai.VisualRig.FacingDirection+" cam="+(ai.VisualRig.Cam!=null));
            }
            var snapshot=AiGroupOrders.Capture(AiRecipients.All,null,host);
            Assert.That(snapshot.Select("red",AiRecipients.All,null).Count,Is.Zero,"Human red slot must never be selected");
            Assert.That(snapshot.Select("blue",AiRecipients.All,null).Count,Is.EqualTo(1));
            Assert.That(snapshot.Select("two",AiRecipients.All,null).Count,Is.EqualTo(2));
            ais[0].VisualRig.Teleport(new Vector3(0,.1f,10),Quaternion.identity);
            yield return new WaitForSeconds(.5f);
            Assert.That(AiGroupOrders.Capture(AiRecipients.Selected,ais[0],host).Execute("greet",host,true),Does.Contain("1/1명"));
            Assert.That(ais[0].AiGreetingActive,Is.False,"Do not wave before arriving");
            until=Time.realtimeSinceStartup+20;
            while(!ais[0].AiGreetingActive && ais[0].AiBusy && Time.realtimeSinceStartup<until) yield return null;
            Assert.That(ais[0].AiGreetingActive,Is.True,ais[0].AiActivity);
            Assert.That(Vector3.Distance(ais[0].VisualRig.FootPosition,host.FootPosition),Is.LessThan(4));
            manager.StopHost(); Object.Destroy(root); Object.Destroy(lobby.gameObject);
            yield return new ExitPlayMode();
        }

        static bool AllFacing(NetPlayer[] ais,PlayerRig host)
        {
            foreach(var ai in ais)
                if(Vector3.Angle(ai.VisualRig.FacingDirection,Vector3.ProjectOnPlane(host.FootPosition-ai.VisualRig.FootPosition,Vector3.up))>15) return false;
            return true;
        }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(NetworkManager.singleton!=null && NetworkServer.active) NetworkManager.singleton.StopHost();
            if(Application.isPlaying) yield return new ExitPlayMode();
            TeamSlots.Clear(); AiHostSettings.BeginRoom(); AiHostSettings.BindRuntime(null);
        }
    }
}
