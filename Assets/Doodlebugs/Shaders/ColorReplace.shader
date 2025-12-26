Shader "Custom/ColorReplace"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _SourceColor ("Source Color", Color) = (1, 0, 0, 1)
        _TargetColor ("Target Color", Color) = (0, 0, 1, 1)
        _Threshold ("Color Threshold", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _SourceColor;
            float4 _TargetColor;
            float _Threshold;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv) * i.color;

                // Calculate color distance from source color
                float dist = distance(col.rgb, _SourceColor.rgb);

                // If close enough to source color, replace with target color
                if (dist < _Threshold)
                {
                    // Blend based on how close the color is
                    float blend = 1.0 - (dist / _Threshold);
                    col.rgb = lerp(col.rgb, _TargetColor.rgb, blend);
                }

                return col;
            }
            ENDCG
        }
    }
}
