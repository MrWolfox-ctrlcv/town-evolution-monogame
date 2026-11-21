#if OPENGL
    #define SV_POSITION POSITION
    #define VS_MODEL vs_3_0
    #define PS_MODEL ps_3_0
#else
    #define VS_MODEL vs_4_0
    #define PS_MODEL ps_4_0
#endif

float4x4 ViewProjection;
float3 LightDirection = float3(-0.45, -0.82, 0.35);
float3 AmbientColor = float3(0.66, 0.69, 0.62);
float3 Tint = float3(1, 1, 1);
float Opacity;
texture Palette;
sampler PaletteSampler = sampler_state
{
    Texture = <Palette>;
    MinFilter = LINEAR; MagFilter = LINEAR; MipFilter = NONE;
    AddressU = CLAMP; AddressV = CLAMP;
};
struct VertexInput
{
    float4 Position : POSITION0;
    float3 Normal : NORMAL0;
    float2 UV : TEXCOORD0;
    float4 Instance0 : TEXCOORD4;
    float4 Instance1 : TEXCOORD5;
    float4 Instance2 : TEXCOORD6;
    float4 Instance3 : TEXCOORD7;
};
struct PixelInput
{
    float4 Position : SV_POSITION;
    float2 UV : TEXCOORD0;
    float3 Lighting : TEXCOORD1;
};
PixelInput InstancedVS(VertexInput input)
{
    PixelInput output;
    float4x4 world = float4x4(input.Instance0, input.Instance1, input.Instance2, input.Instance3);
    float4 p = mul(input.Position, world);
    float3 normal = normalize(mul(input.Normal, (float3x3)world));
    output.Position = mul(p, ViewProjection);
    output.UV = input.UV;
    output.Lighting = AmbientColor + max(0, dot(normal, -normalize(LightDirection))) * float3(0.38, 0.35, 0.29);
    return output;
}
float4 InstancedPS(PixelInput input) : COLOR0
{
    float4 color = tex2D(PaletteSampler, input.UV);
    clip(color.a - 0.3);
    return float4(color.rgb * input.Lighting * Tint, Opacity);
}
technique Instanced
{
    pass P0
    {
        VertexShader = compile VS_MODEL InstancedVS();
        PixelShader = compile PS_MODEL InstancedPS();
    }
}