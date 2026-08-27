// 반투명 표시용 — 스냅 소켓 미리보기, 포털 링, 세계 포식자의 몸.
// 프레넬로 실루엣을 살려서 반투명인데도 형태가 읽히게 한다.
Shader "SpaceFlop/Ghost"
{
    Properties
    {
        _Color ("색", Color) = (0.55, 0.95, 0.70, 0.35)
        _RimPower ("Rim Power", Range(0.5, 6)) = 2.2
        _RimBoost ("Rim Boost", Range(0, 4)) = 1.6
        _Pulse ("Pulse", Range(0, 2)) = 0.0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            half _RimPower;
            half _RimBoost;
            half _Pulse;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDir = normalize(_WorldSpaceCameraPos - worldPos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half rim = 1.0 - saturate(dot(normalize(i.worldNormal), normalize(i.viewDir)));
                rim = pow(rim, _RimPower) * _RimBoost;

                half pulse = 1.0 + _Pulse * 0.35 * sin(_Time.y * 3.4);

                fixed4 c = _Color;
                c.rgb *= (0.75 + rim) * pulse;
                c.a = saturate(c.a * pulse + rim * 0.55);
                return c;
            }
            ENDCG
        }
    }

    FallBack Off
}
