﻿sampler uImage0 : register(s0);
sampler uImage1 : register(s1);
float3 uColor;
float3 uSecondaryColor;
float uOpacity;
float uSaturation;
float uRotation;
float uTime;
float4 uSourceRect;
float2 uWorldPosition;
float uDirection;
float3 uLightSource;
float2 uImageSize0;
float2 uImageSize1;
float2 uOffset;
float uScale;
float2 uTargetPosition;
float4 uLegacyArmorSourceRect;
float2 uLegacyArmorSheetSize;
float4 uShaderSpecificData;
/*float2 uMin;
float2 uMax;*/

float4 Mapper(float4 sampleColor : COLOR0, float2 coords : TEXCOORD0) : COLOR0 {
	float4 color = tex2D(uImage0, coords);
	if (color.b == 0) color = tex2D(uImage1, color.rg) * color.a;
	return color;
}

technique Technique1 {
	pass Mapper {
		PixelShader = compile ps_2_0 Mapper();
	}
}