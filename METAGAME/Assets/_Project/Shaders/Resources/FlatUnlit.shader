// 빛을 받지 않는 순수 색. 별밭·먼 행성 테두리·표시등처럼
// "저 멀리서 스스로 빛나는 것" 에 쓴다. 정점 색을 곱하므로 별밭은 메시 하나로 끝난다.
Shader "SpaceFlop/Flat"
{
    Properties
    {
        _Color ("색", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry-100" }
        LOD 100

        Pass
        {
            ZWrite On
            Cull Off
            Fog { Mode Off }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return _Color * i.color;
            }
            ENDCG
        }
    }

    FallBack Off
}
