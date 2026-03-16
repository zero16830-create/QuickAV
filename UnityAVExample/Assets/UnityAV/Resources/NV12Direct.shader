Shader "UnityAV/NV12Direct"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "black" {}
        _YPlane ("Y Plane", 2D) = "black" {}
        _UVPlane ("UV Plane", 2D) = "gray" {}
        _UseNativeVideoPlaneTextures ("Use Native Video Plane Textures", Float) = 0
        _FlipVertical ("Flip Vertical", Float) = 1
        _VideoSourcePixelFormat ("Video Source Pixel Format", Float) = 2
        _VideoColorRange ("Video Color Range", Float) = 0
        _VideoColorMatrix ("Video Color Matrix", Float) = 1
        _VideoColorPrimaries ("Video Color Primaries", Float) = 1
        _VideoTransfer ("Video Transfer", Float) = 0
        _VideoBitDepth ("Video Bit Depth", Float) = 8
        _VideoDynamicRange ("Video Dynamic Range", Float) = 1
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
            float _VideoSourcePixelFormat;
            float _VideoColorRange;
            float _VideoColorMatrix;
            float _VideoColorPrimaries;
            float _VideoTransfer;
            float _VideoBitDepth;
            float _VideoDynamicRange;

            static const float VideoColorRangeLimited = 0.0;
            static const float VideoColorMatrixBt601 = 0.0;
            static const float VideoColorMatrixBt709 = 1.0;
            static const float VideoColorMatrixBt2020Ncl = 2.0;
            static const float VideoColorMatrixBt2020Cl = 3.0;
            static const float VideoTransferLinear = 2.0;
            static const float VideoTransferPq = 4.0;
            static const float VideoDynamicRangeHdr10 = 2.0;

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

            float2 ResolveYuvCoefficients(float matrixKind)
            {
                if (abs(matrixKind - VideoColorMatrixBt601) < 0.5)
                {
                    return float2(0.2990, 0.1140);
                }

                if (abs(matrixKind - VideoColorMatrixBt2020Ncl) < 0.5
                    || abs(matrixKind - VideoColorMatrixBt2020Cl) < 0.5)
                {
                    return float2(0.2627, 0.0593);
                }

                return float2(0.2126, 0.0722);
            }

            void NormalizeYuvSample(
                float yEncoded,
                float2 uvEncoded,
                out float luma,
                out float2 chroma)
            {
                float bitDepth = max(_VideoBitDepth, 8.0);
                float denom = bitDepth >= 10.0 ? 1023.0 : 255.0;
                float yOffset = bitDepth >= 10.0 ? (64.0 / denom) : (16.0 / denom);
                float yScale = bitDepth >= 10.0 ? (denom / 876.0) : (255.0 / 219.0);
                float uvScale = bitDepth >= 10.0 ? (denom / 896.0) : (255.0 / 224.0);

                if (abs(_VideoColorRange - VideoColorRangeLimited) < 0.5)
                {
                    luma = saturate((yEncoded - yOffset) * yScale);
                    chroma = (uvEncoded - 0.5) * uvScale;
                }
                else
                {
                    luma = saturate(yEncoded);
                    chroma = uvEncoded - 0.5;
                }
            }

            float3 ConvertYuvToRgb(float yEncoded, float2 uvEncoded)
            {
                float luma;
                float2 chroma;
                NormalizeYuvSample(yEncoded, uvEncoded, luma, chroma);

                float2 coeffs = ResolveYuvCoefficients(_VideoColorMatrix);
                float kr = coeffs.x;
                float kb = coeffs.y;
                float kg = max(1.0 - kr - kb, 1e-6);

                float cb = chroma.x;
                float cr = chroma.y;

                float3 rgb;
                rgb.r = luma + 2.0 * (1.0 - kr) * cr;
                rgb.b = luma + 2.0 * (1.0 - kb) * cb;
                rgb.g = luma
                    - (2.0 * kb * (1.0 - kb) / kg) * cb
                    - (2.0 * kr * (1.0 - kr) / kg) * cr;
                return rgb;
            }

            float3 PqToLinear(float3 encoded)
            {
                const float m1 = 2610.0 / 16384.0;
                const float m2 = 2523.0 / 32.0;
                const float c1 = 3424.0 / 4096.0;
                const float c2 = 2413.0 / 128.0;
                const float c3 = 2392.0 / 128.0;

                float3 safeEncoded = max(encoded, 0.0);
                float3 powerTerm = pow(safeEncoded, 1.0 / m2);
                float3 numerator = max(powerTerm - c1, 0.0);
                float3 denominator = max(c2 - c3 * powerTerm, 1e-6);
                return pow(numerator / denominator, 1.0 / m1);
            }

            float3 ConvertBt2020ToBt709(float3 linearBt2020)
            {
                return float3(
                    dot(float3(1.6605, -0.5876, -0.0728), linearBt2020),
                    dot(float3(-0.1246, 1.1329, -0.0083), linearBt2020),
                    dot(float3(-0.0182, -0.1006, 1.1187), linearBt2020));
            }

            float3 ToneMapHdr10ToSdr(float3 linearRgb)
            {
                float3 exposed = max(linearRgb, 0.0) * 48.0;
                float3 numerator = exposed * (2.51 * exposed + 0.03);
                float3 denominator = exposed * (2.43 * exposed + 0.59) + 0.14;
                return saturate(numerator / max(denominator, 1e-6));
            }

            float3 LinearToSrgb(float3 linearRgb)
            {
                float3 safeRgb = saturate(linearRgb);
                float3 lower = safeRgb * 12.92;
                float3 upper = 1.055 * pow(safeRgb, 1.0 / 2.4) - 0.055;
                return lerp(upper, lower, step(safeRgb, 0.0031308));
            }

            float3 PrepareDisplayRgb(float3 encodedRgb)
            {
                bool isHdr10 = abs(_VideoDynamicRange - VideoDynamicRangeHdr10) < 0.5
                    || abs(_VideoTransfer - VideoTransferPq) < 0.5;

                if (isHdr10)
                {
                    float3 linearBt2020 = PqToLinear(saturate(encodedRgb));
                    float3 linearBt709 = ConvertBt2020ToBt709(linearBt2020);
                    return LinearToSrgb(ToneMapHdr10ToSdr(linearBt709));
                }

                if (abs(_VideoTransfer - VideoTransferLinear) < 0.5)
                {
                    return LinearToSrgb(encodedRgb);
                }

                return saturate(encodedRgb);
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
                    float3 rgb = ConvertYuvToRgb(y, uvSample);
                    rgb = PrepareDisplayRgb(rgb);
                    return float4(rgb, 1.0);
                }

                return tex2D(_MainTex, uv);
            }
            ENDCG
        }
    }

    FallBack Off
}
