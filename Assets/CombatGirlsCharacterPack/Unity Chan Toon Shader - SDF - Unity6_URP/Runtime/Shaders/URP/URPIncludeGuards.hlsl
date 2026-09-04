#ifndef URP_INCLUDE_GUARDS_INCLUDED
#define URP_INCLUDE_GUARDS_INCLUDED

//This file is used to block include files inside com.unity.render-pipelines.universal package due to 
//custom toon requirements

#define UNIVERSAL_LIT_INPUT_INCLUDED //LitInput.hlsl: Toon has a custom CBUFFER

//URP 17.6 moved the parameterless IsSurfaceTypeTransparent() into Shaders/Utils/SurfaceType.hlsl,
//which URP reaches only through LitInput.hlsl - blocked above. LitForwardPass.hlsl still calls it,
//so supply it here against Toon's own _Surface, and claim SurfaceType.hlsl's guard so that if a
//later URP include pulls it in, it cannot redeclare _Surface on top of the Toon CBUFFER.
#ifndef UNIVERSAL_SURFACE_TYPE_TRANSPARENT_INCLUDED
#define UNIVERSAL_SURFACE_TYPE_TRANSPARENT_INCLUDED
inline bool IsSurfaceTypeTransparent() { return _Surface > 0; }
inline bool IsSurfaceTypeOpaque()      { return !IsSurfaceTypeTransparent(); }
#endif // UNIVERSAL_SURFACE_TYPE_TRANSPARENT_INCLUDED

#endif // URP_INCLUDE_GUARDS_INCLUDED