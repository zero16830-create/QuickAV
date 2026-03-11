Shader "UnityAV/NV12Direct"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "black" {}
        _YPlane ("Y Plane", 2D) = "black" {}
        _UVPlane ("UV Plane", 2D) = "gray" {}
        _UseNativeVideoPlaneTextures ("Use Native Video Plane Textures", Float) = 0
        _FlipVertical ("Flip Vertical", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Off
        Lighting Off
        ZWrite On
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _YPlane;
            sampler2D _UVPlane;
            float4 _MainTex_ST;
            float _UseNativeVideoPlaneTextures;
            float _FlipVertical;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            float3 yuv_to_rgb(float y, float u, float v)
            {
                float3 rgb;
                rgb.r = y + 1.5748 * v;
                rgb.g = y - 0.1873 * u - 0.4681 * v;
                rgb.b = y + 1.8556 * u;
                return saturate(rgb);
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 uv = input.uv;
                if (_UseNativeVideoPlaneTextures > 0.5)
                {
                    if (_FlipVertical > 0.5)
                    {
                        uv.y = 1.0 - uv.y;
                    }

                    float y = tex2D(_YPlane, uv).r;
                    float2 uvSample = tex2D(_UVPlane, uv).rg;
                    float u = uvSample.x - 0.5;
                    float v = uvSample.y - 0.5;
                    float3 rgb = yuv_to_rgb(y, u, v);
                    return float4(rgb, 1.0);
                }

                return tex2D(_MainTex, uv);
            }
            ENDCG
        }
    }

    FallBack Off
}
