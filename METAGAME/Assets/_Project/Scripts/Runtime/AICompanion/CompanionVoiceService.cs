using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace Metagame.AICompanion
{
    /// <summary>
    /// 말한 것을 이 PC 의 음성·행동 모델에 보내고 <c>action</c> 하나를 받아 온다.
    ///
    /// <para><b>바깥 API 를 쓰지 않는다.</b> 주소는 언제나 이 PC 다(CLAUDE.md: 유료 API 호출
    /// 금지, 로컬 Whisper·Qwen 유지). 음성이 기기 밖으로 나가지 않는 것이 팀 결정이다.</para>
    ///
    /// <para>이 클래스는 <b>주고받기만</b> 한다. 누구에게 시킬지, 무엇을 할지는 부르는 쪽이
    /// 정한다.</para>
    /// </summary>
    public sealed class CompanionVoiceService : MonoBehaviour
    {
        /// <summary>이 PC 의 음성·행동 서버. 두 곳에 나눠 적어 두면 한쪽만 고치게 된다.</summary>
        public const string BaseUrl = "http://127.0.0.1:8765/api/companion-v4";

        /// <summary>한 번에 담을 수 있는 말의 길이(초). 넘으면 잘라 보낸다.</summary>
        public const int MaxSeconds = 8;

        /// <summary>이보다 짧으면 잘못 누른 것으로 보고 보내지 않는다(초).</summary>
        const float MinSeconds = 0.2f;

        /// <summary>서버가 쓰는 녹음 형식. 바꾸면 서버도 같이 고쳐야 한다.</summary>
        const int SampleRate = 16000;

        [Serializable]
        sealed class TextRequest { public string text; public string model; }

        [Serializable]
        sealed class Reply { public string text; public string action; }

        /// <summary>결과. <paramref name="text"/> 는 들린 말, <paramref name="action"/> 은 고른 행동.</summary>
        public delegate void Heard(string text, string action, string error);

        public bool Busy { get; private set; }

        [SerializeField] string _model = "qwen3:1.7b";

        AudioClip _recording;
        string _device;
        float _recordStart;

        /// <summary>녹음 중인가. 화면에 표시할 때 쓴다.</summary>
        public bool Recording => _recording != null;

        /// <summary>녹음을 시작한다. 마이크가 없으면 <c>false</c>.</summary>
        public bool StartRecording(out string problem)
        {
            problem = null;
            if (Busy) { problem = "아직 앞 요청을 처리하는 중이에요"; return false; }
            if (Microphone.devices.Length == 0) { problem = "마이크가 없어요"; return false; }

            _device = Microphone.devices[0];
            try
            {
                _recording = Microphone.Start(_device, false, MaxSeconds, SampleRate);
                _recordStart = Time.realtimeSinceStartup;
                return true;
            }
            catch (Exception e)
            {
                problem = "마이크를 열지 못했어요: " + e.Message;
                _recording = null;
                return false;
            }
        }

        /// <summary>녹음을 멈추고 보낸다. 너무 짧으면 버린다.</summary>
        public void FinishRecording(Heard onHeard)
        {
            if (_recording == null) return;

            int length = Microphone.GetPosition(_device);
            Microphone.End(_device);
            AudioClip clip = _recording;
            _recording = null;

            if (clip == null) return;
            if (length < clip.frequency * MinSeconds)
            {
                Destroy(clip);
                onHeard?.Invoke(null, null, "너무 짧아요 · V 를 누른 채 말해 주세요");
                return;
            }

            var samples = new float[length * clip.channels];
            clip.GetData(samples, 0);
            byte[] wave = CompanionPolicy.EncodeWave(samples, clip.channels, clip.frequency);
            Destroy(clip);

            var form = new WWWForm();
            form.AddBinaryData("audio", wave, "command.wav", "audio/wav");
            form.AddField("model", _model);
            StartCoroutine(Send(UnityWebRequest.Post(BaseUrl + "/voice", form), onHeard));
        }

        /// <summary>녹음이 상한을 넘겼나. 넘겼으면 부르는 쪽이 끊어 준다.</summary>
        public bool RecordingTooLong =>
            _recording != null && Time.realtimeSinceStartup - _recordStart >= MaxSeconds - 0.5f;

        /// <summary>
        /// 말 대신 글로 보낸다. <b>음성 인식을 건너뛴다.</b>
        ///
        /// <para>STT 가 틀렸을 때 <b>인식 실패인지 행동 버그인지 가르는 유일한 수단</b>이다.
        /// 출시 빌드에서는 부르는 쪽이 <c>#if</c> 로 막는다.</para>
        /// </summary>
        public void SendText(string text, Heard onHeard)
        {
            if (Busy || string.IsNullOrWhiteSpace(text)) return;

            string payload = JsonUtility.ToJson(new TextRequest { text = text, model = _model });
            var request = new UnityWebRequest(BaseUrl + "/text", "POST")
            {
                uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(payload)),
                downloadHandler = new DownloadHandlerBuffer(),
            };
            request.SetRequestHeader("Content-Type", "application/json");
            StartCoroutine(Send(request, onHeard));
        }

        IEnumerator Send(UnityWebRequest request, Heard onHeard)
        {
            Busy = true;
            request.timeout = 30;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onHeard?.Invoke(null, null, CompanionPolicy.ServiceError(request.responseCode));
            }
            else
            {
                Reply reply = null;
                try { reply = JsonUtility.FromJson<Reply>(request.downloadHandler.text); }
                catch (Exception) { /* 아래에서 없는 것으로 처리한다 */ }

                if (reply == null) onHeard?.Invoke(null, null, "서버 응답을 읽지 못했어요");
                else onHeard?.Invoke(reply.text, reply.action, null);
            }

            request.Dispose();
            Busy = false;
        }
    }
}
