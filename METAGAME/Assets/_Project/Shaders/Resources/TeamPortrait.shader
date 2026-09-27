Shader "NotThisPlanet/TeamPortrait"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Cull Off ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST, _Tint, _Color;
            float4x4 _PortraitMvp, _PortraitNormal;
            struct Input { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct Output { float4 position : SV_POSITION; float2 uv : TEXCOORD0; float3 normal : TEXCOORD1; float2 circle : TEXCOORD2; };
            Output Vert(Input v)
            {
                Output o;
                o.position = mul(_PortraitMvp, v.vertex);
                o.circle = o.position.xy / o.position.w;
                o.normal = normalize(mul((float3x3)_PortraitNormal, v.normal));
                o.uv = v.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                return o;
            }
            half4 Frag(Output i) : SV_Target
            {
                float edge = 1 - length(i.circle);
                clip(edge);
                half3 color = tex2D(_MainTex, i.uv).rgb * _Color.rgb;
                half chroma = max(color.r, max(color.g, color.b)) - min(color.r, min(color.g, color.b));
                half whiteMask = (1 - smoothstep(.10, .24, chroma))
                    * smoothstep(.52, .78, dot(color, half3(.299, .587, .114)));
                color *= lerp(half3(1,1,1), _Tint.rgb, whiteMask);
                color *= .72 + .28 * saturate(dot(normalize(i.normal), normalize(float3(-.4, .7, 1))));
                return half4(color, saturate(edge * 64));
            }
            ENDHLSL
        }
    }
}
