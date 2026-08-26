// 말랑한 장난감 인형 룩. Built-in Render Pipeline 용 서피스 셰이더.
//  · 랩 라이팅 + 얕은 서브서페이스로 "말랑한 점토" 느낌
//  · _Softness 로 촉촉한 찰흙(1) ↔ 단단한 돌/금속(0) 을 보간
//  · 월드 좌표 노이즈로 손자국 같은 얼룩
Shader "SpaceFlop/Dough"
{
    Properties
    {
        _Color        ("색",         Color) = (0.90, 0.62, 0.50, 1)
        _Softness     ("Softness",   Range(0,1)) = 0.85
        _Wrap         ("Light Wrap", Range(0,1)) = 0.55
        _SSS          ("Subsurface", Range(0,2)) = 0.6
        _Gloss        ("Gloss",      Range(0,1)) = 0.45
        _Mottle       ("얼룩",       Range(0,1)) = 0.22
        _MottleScale  ("얼룩 크기",  Range(0.1,24)) = 1.4
        _Ambient      ("Ambient",    Range(0,2)) = 0.45
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 250

        CGPROGRAM
        #pragma surface surf ClayWrap fullforwardshadows addshadow
        #pragma target 3.0

        fixed4 _Color;
        half _Softness;
        half _Wrap;
        half _SSS;
        half _Gloss;
        half _Mottle;
        half _MottleScale;
        half _Ambient;

        struct Input
        {
            float3 worldPos;
        };

        float hash31(float3 p)
        {
            return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453);
        }

        float vnoise(float3 p)
        {
            float3 i = floor(p);
            float3 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);

            float n000 = hash31(i);
            float n100 = hash31(i + float3(1,0,0));
            float n010 = hash31(i + float3(0,1,0));
            float n110 = hash31(i + float3(1,1,0));
            float n001 = hash31(i + float3(0,0,1));
            float n101 = hash31(i + float3(1,0,1));
            float n011 = hash31(i + float3(0,1,1));
            float n111 = hash31(i + float3(1,1,1));

            float nx00 = lerp(n000, n100, f.x);
            float nx10 = lerp(n010, n110, f.x);
            float nx01 = lerp(n001, n101, f.x);
            float nx11 = lerp(n011, n111, f.x);
            return lerp(lerp(nx00, nx10, f.y), lerp(nx01, nx11, f.y), f.z);
        }

        half4 LightingClayWrap(SurfaceOutput s, half3 lightDir, half3 viewDir, half atten)
        {
            half3 n = normalize(s.Normal);
            half3 l = normalize(lightDir);
            half3 v = normalize(viewDir);

            // 말랑할수록 빛이 더 부드럽게 감긴다
            half w = lerp(0.18, _Wrap, _Softness);
            half ndl = dot(n, l);
            half diff = saturate((ndl + w) / (1.0 + w));

            half3 hv = normalize(l + v);
            half specPow = lerp(46.0, 22.0, _Softness);
            half spec = pow(saturate(dot(n, hv)), specPow) * _Gloss;

            // 얕은 투과광 — 찰흙일 때만 눈에 보이게
            half back = pow(saturate(dot(v, -l)), 2.0) * _SSS * _Softness * 0.45;

            half3 col = s.Albedo * _LightColor0.rgb * diff * atten;
            col += _LightColor0.rgb * spec * atten;
            col += s.Albedo * _LightColor0.rgb * back * atten;

            half4 c;
            c.rgb = col;
            c.a = s.Alpha;
            return c;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            // 저주파 위주 — 고주파를 세게 섞으면 깨알 같은 노이즈로 보인다
            float n = vnoise(IN.worldPos * _MottleScale);
            float n2 = vnoise(IN.worldPos * _MottleScale * 2.3);
            float mottle = lerp(1.0 - _Mottle * 0.30, 1.0 + _Mottle * 0.16, n * 0.82 + n2 * 0.18);

            o.Albedo = _Color.rgb * mottle;
            o.Alpha = 1.0;

            // 커스텀 라이팅이라 앰비언트는 직접 얹는다
            o.Emission = o.Albedo * unity_AmbientSky.rgb * _Ambient * lerp(0.75, 1.0, _Softness);
        }
        ENDCG
    }

    FallBack "Diffuse"
}
