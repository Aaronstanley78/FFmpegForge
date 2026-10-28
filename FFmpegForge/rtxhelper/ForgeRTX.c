/*
 * ForgeRTX.exe - FFmpeg Forge RTX Video helper (driver engine)
 *
 * Applies NVIDIA RTX Video Super Resolution and/or RTX Video HDR (TrueHDR) to raw
 * video frames through the D3D11 video processor + NVIDIA driver extensions
 * (same mechanism as Chrome, VLC and mpv). No SDK DLL needed: only an RTX GPU and
 * a driver with "RTX Video Enhancement" enabled.
 *
 * stdin : raw NV12 frames  (w x h)
 * stdout: raw BGRA frames (W x H)            when -hdr 0
 *         raw R10G10B10A2 = x2bgr10le (W x H) when -hdr 1  (BT.2020 / PQ)
 *
 * usage: ForgeRTX.exe -w W -h H -ow OW -oh OH [-vsr 0|1] [-hdr 0|1] [-csp 601|709]
 *                     [-fps NUM DEN] [-adapter N]
 *        ForgeRTX.exe -probe
 */
#define COBJMACROS
#define INITGUID
#include <windows.h>
#include <initguid.h>
#include <d3d11.h>
#include <d3d11_1.h>
#include <dxgi1_2.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <fcntl.h>
#include <io.h>

DEFINE_GUID(NV_PPE_INTERFACE_GUID, 0xd43ce1b3, 0x1f4b, 0x48ac, 0xba, 0xee, 0xc3, 0xc2, 0x53, 0x75, 0xe6, 0xf7);
DEFINE_GUID(NV_TRUE_HDR_INTERFACE_GUID, 0xfdd62bb4, 0x620b, 0x4fd7, 0x9a, 0xb3, 0x1e, 0x59, 0xd0, 0xd5, 0x44, 0xb3);

typedef struct { UINT version; UINT method; UINT enable; } nv_ext;

static void die(const char *what, HRESULT hr)
{
    fprintf(stderr, "ForgeRTX: ERROR %s (hr=0x%08lx)\n", what, (unsigned long)hr);
    fflush(stderr);
    exit(2);
}

static int read_full(FILE *f, unsigned char *buf, size_t n)
{
    size_t got = 0;
    while (got < n) {
        size_t r = fread(buf + got, 1, n - got, f);
        if (r == 0) return got == 0 ? 0 : -1;
        got += r;
    }
    return 1;
}

static IDXGIAdapter *pick_adapter(int want, char *name, size_t namelen, UINT *vendor)
{
    IDXGIFactory1 *fac = NULL;
    IDXGIAdapter1 *ad = NULL, *best = NULL;
    if (FAILED(CreateDXGIFactory1(&IID_IDXGIFactory1, (void **)&fac))) return NULL;
    for (UINT i = 0; IDXGIFactory1_EnumAdapters1(fac, i, &ad) != DXGI_ERROR_NOT_FOUND; i++) {
        DXGI_ADAPTER_DESC1 d;
        IDXGIAdapter1_GetDesc1(ad, &d);
        int take = 0;
        if (want >= 0) take = ((int)i == want);
        else take = (best == NULL && d.VendorId == 0x10DE && !(d.Flags & DXGI_ADAPTER_FLAG_SOFTWARE));
        if (take) {
            best = ad;
            *vendor = d.VendorId;
            WideCharToMultiByte(CP_UTF8, 0, d.Description, -1, name, (int)namelen, NULL, NULL);
        } else IDXGIAdapter1_Release(ad);
    }
    IDXGIFactory1_Release(fac);
    return (IDXGIAdapter *)best;
}

int main(int argc, char **argv)
{
    int w = 0, h = 0, ow = 0, oh = 0, vsr = 1, hdr = 0, csp = 709, adapter = -1, probe = 0;
    UINT fpsn = 30, fpsd = 1;
    for (int i = 1; i < argc; i++) {
        if (!strcmp(argv[i], "-w") && i + 1 < argc) w = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-h") && i + 1 < argc) h = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-ow") && i + 1 < argc) ow = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-oh") && i + 1 < argc) oh = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-vsr") && i + 1 < argc) vsr = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-hdr") && i + 1 < argc) hdr = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-csp") && i + 1 < argc) csp = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-adapter") && i + 1 < argc) adapter = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-fps") && i + 2 < argc) { fpsn = (UINT)atoi(argv[++i]); fpsd = (UINT)atoi(argv[++i]); }
        else if (!strcmp(argv[i], "-probe")) probe = 1;
    }
    if (probe) { w = 1280; h = 720; ow = 2560; oh = 1440; }
    if (w <= 0 || h <= 0 || ow <= 0 || oh <= 0 || (w & 1) || (h & 1)) {
        fprintf(stderr, "ForgeRTX: invalid sizes (input must be even): %dx%d -> %dx%d\n", w, h, ow, oh);
        return 1;
    }
    if (fpsn == 0 || fpsd == 0) { fpsn = 30; fpsd = 1; }

    char name[256] = "default";
    UINT vendor = 0;
    IDXGIAdapter *ad = pick_adapter(adapter, name, sizeof(name), &vendor);

    ID3D11Device *dev = NULL;
    ID3D11DeviceContext *ctx = NULL;
    D3D_FEATURE_LEVEL fl;
    UINT flags = D3D11_CREATE_DEVICE_VIDEO_SUPPORT | D3D11_CREATE_DEVICE_BGRA_SUPPORT;
    HRESULT hr = D3D11CreateDevice(ad, ad ? D3D_DRIVER_TYPE_UNKNOWN : D3D_DRIVER_TYPE_HARDWARE, NULL, flags,
                                   NULL, 0, D3D11_SDK_VERSION, &dev, &fl, &ctx);
    if (FAILED(hr)) die("D3D11CreateDevice", hr);
    fprintf(stderr, "ForgeRTX: GPU \"%s\" (vendor 0x%04X)%s\n", name, vendor, vendor == 0x10DE ? "" : "  -- not an NVIDIA GPU: RTX effects will not apply");

    ID3D11VideoDevice *vdev = NULL;
    ID3D11VideoContext *vctx = NULL;
    ID3D11VideoContext1 *vctx1 = NULL;
    if (FAILED(hr = ID3D11Device_QueryInterface(dev, &IID_ID3D11VideoDevice, (void **)&vdev))) die("ID3D11VideoDevice", hr);
    if (FAILED(hr = ID3D11DeviceContext_QueryInterface(ctx, &IID_ID3D11VideoContext, (void **)&vctx))) die("ID3D11VideoContext", hr);
    ID3D11VideoContext_QueryInterface(vctx, &IID_ID3D11VideoContext1, (void **)&vctx1);

    D3D11_VIDEO_PROCESSOR_CONTENT_DESC cd;
    memset(&cd, 0, sizeof(cd));
    cd.InputFrameFormat = D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE;
    cd.InputFrameRate.Numerator = fpsn; cd.InputFrameRate.Denominator = fpsd;
    cd.InputWidth = w; cd.InputHeight = h;
    cd.OutputFrameRate.Numerator = fpsn; cd.OutputFrameRate.Denominator = fpsd;
    cd.OutputWidth = ow; cd.OutputHeight = oh;
    cd.Usage = D3D11_VIDEO_USAGE_OPTIMAL_QUALITY;
    ID3D11VideoProcessorEnumerator *ven = NULL;
    ID3D11VideoProcessor *vp = NULL;
    if (FAILED(hr = ID3D11VideoDevice_CreateVideoProcessorEnumerator(vdev, &cd, &ven))) die("CreateVideoProcessorEnumerator", hr);
    if (FAILED(hr = ID3D11VideoDevice_CreateVideoProcessor(vdev, ven, 0, &vp))) die("CreateVideoProcessor", hr);

    DXGI_FORMAT outFmt = hdr ? DXGI_FORMAT_R10G10B10A2_UNORM : DXGI_FORMAT_B8G8R8A8_UNORM;

    /* textures */
    D3D11_TEXTURE2D_DESC td;
    memset(&td, 0, sizeof(td));
    td.Width = w; td.Height = h; td.MipLevels = 1; td.ArraySize = 1;
    td.Format = DXGI_FORMAT_NV12; td.SampleDesc.Count = 1;
    td.Usage = D3D11_USAGE_DEFAULT; td.BindFlags = D3D11_BIND_DECODER;
    ID3D11Texture2D *inTex = NULL, *inStage = NULL, *outTex = NULL, *outStage = NULL;
    if (FAILED(hr = ID3D11Device_CreateTexture2D(dev, &td, NULL, &inTex))) {
        td.BindFlags = D3D11_BIND_SHADER_RESOURCE;
        if (FAILED(hr = ID3D11Device_CreateTexture2D(dev, &td, NULL, &inTex))) die("CreateTexture2D(input NV12)", hr);
    }
    td.Usage = D3D11_USAGE_STAGING; td.BindFlags = 0; td.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    if (FAILED(hr = ID3D11Device_CreateTexture2D(dev, &td, NULL, &inStage))) die("CreateTexture2D(input staging)", hr);

    memset(&td, 0, sizeof(td));
    td.Width = ow; td.Height = oh; td.MipLevels = 1; td.ArraySize = 1;
    td.Format = outFmt; td.SampleDesc.Count = 1;
    td.Usage = D3D11_USAGE_DEFAULT; td.BindFlags = D3D11_BIND_RENDER_TARGET;
    if (FAILED(hr = ID3D11Device_CreateTexture2D(dev, &td, NULL, &outTex))) die("CreateTexture2D(output)", hr);
    td.Usage = D3D11_USAGE_STAGING; td.BindFlags = 0; td.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    if (FAILED(hr = ID3D11Device_CreateTexture2D(dev, &td, NULL, &outStage))) die("CreateTexture2D(output staging)", hr);

    /* views */
    D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC ivd;
    memset(&ivd, 0, sizeof(ivd));
    ivd.ViewDimension = D3D11_VPIV_DIMENSION_TEXTURE2D;
    ID3D11VideoProcessorInputView *iv = NULL;
    if (FAILED(hr = ID3D11VideoDevice_CreateVideoProcessorInputView(vdev, (ID3D11Resource *)inTex, ven, &ivd, &iv))) die("CreateVideoProcessorInputView", hr);
    D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC ovd;
    memset(&ovd, 0, sizeof(ovd));
    ovd.ViewDimension = D3D11_VPOV_DIMENSION_TEXTURE2D;
    ID3D11VideoProcessorOutputView *ov = NULL;
    if (FAILED(hr = ID3D11VideoDevice_CreateVideoProcessorOutputView(vdev, (ID3D11Resource *)outTex, ven, &ovd, &ov))) die("CreateVideoProcessorOutputView", hr);

    /* processor state */
    RECT src = { 0, 0, w, h }, dst = { 0, 0, ow, oh };
    ID3D11VideoContext_VideoProcessorSetStreamFrameFormat(vctx, vp, 0, D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE);
    ID3D11VideoContext_VideoProcessorSetStreamSourceRect(vctx, vp, 0, TRUE, &src);
    ID3D11VideoContext_VideoProcessorSetStreamDestRect(vctx, vp, 0, TRUE, &dst);
    ID3D11VideoContext_VideoProcessorSetOutputTargetRect(vctx, vp, TRUE, &dst);
    ID3D11VideoContext_VideoProcessorSetStreamAutoProcessingMode(vctx, vp, 0, FALSE);
    if (vctx1) {
        ID3D11VideoContext1_VideoProcessorSetStreamColorSpace1(vctx1, vp, 0,
            csp == 601 ? DXGI_COLOR_SPACE_YCBCR_STUDIO_G22_LEFT_P601 : DXGI_COLOR_SPACE_YCBCR_STUDIO_G22_LEFT_P709);
        ID3D11VideoContext1_VideoProcessorSetOutputColorSpace1(vctx1, vp,
            hdr ? DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020 : DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709);
    } else {
        D3D11_VIDEO_PROCESSOR_COLOR_SPACE in_cs, out_cs;
        memset(&in_cs, 0, sizeof(in_cs)); memset(&out_cs, 0, sizeof(out_cs));
        in_cs.YCbCr_Matrix = csp == 601 ? 0 : 1; in_cs.Nominal_Range = D3D11_VIDEO_PROCESSOR_NOMINAL_RANGE_16_235;
        out_cs.RGB_Range = 0; out_cs.Nominal_Range = D3D11_VIDEO_PROCESSOR_NOMINAL_RANGE_0_255;
        ID3D11VideoContext_VideoProcessorSetStreamColorSpace(vctx, vp, 0, &in_cs);
        ID3D11VideoContext_VideoProcessorSetOutputColorSpace(vctx, vp, &out_cs);
        if (hdr) fprintf(stderr, "ForgeRTX: WARNING no ID3D11VideoContext1 - HDR output color space unavailable\n");
    }

    /* NVIDIA driver extensions */
    if (vsr) {
        nv_ext e = { 1, 2, 1 };   /* version 1, method 2 = super resolution, enable */
        hr = ID3D11VideoContext_VideoProcessorSetStreamExtension(vctx, vp, 0, &NV_PPE_INTERFACE_GUID, sizeof(e), &e);
        fprintf(stderr, "ForgeRTX: RTX Video Super Resolution %s (hr=0x%08lx)\n", SUCCEEDED(hr) ? "requested" : "REFUSED", (unsigned long)hr);
    }
    if (hdr) {
        nv_ext e = { 4, 3, 1 };   /* version 4, method 3 = TrueHDR, enable */
        hr = ID3D11VideoContext_VideoProcessorSetStreamExtension(vctx, vp, 0, &NV_TRUE_HDR_INTERFACE_GUID, sizeof(e), &e);
        fprintf(stderr, "ForgeRTX: RTX Video HDR %s (hr=0x%08lx)\n", SUCCEEDED(hr) ? "requested" : "REFUSED", (unsigned long)hr);
    }
    fprintf(stderr, "ForgeRTX: %dx%d NV12 -> %dx%d %s\n", w, h, ow, oh, hdr ? "R10G10B10A2 (HDR10 PQ BT.2020)" : "BGRA");
    fflush(stderr);

    if (probe) {
        printf("OK %s\n", name);
        return 0;
    }

    _setmode(_fileno(stdin), _O_BINARY);
    _setmode(_fileno(stdout), _O_BINARY);
    size_t inSize = (size_t)w * h * 3 / 2;
    size_t outRow = (size_t)ow * 4;
    unsigned char *inBuf = (unsigned char *)malloc(inSize);
    unsigned char *outBuf = (unsigned char *)malloc(outRow * oh);
    if (!inBuf || !outBuf) die("out of memory", E_OUTOFMEMORY);
    setvbuf(stdin, NULL, _IOFBF, 1 << 22);
    setvbuf(stdout, NULL, _IOFBF, 1 << 22);

    D3D11_VIDEO_PROCESSOR_STREAM st;
    memset(&st, 0, sizeof(st));
    st.Enable = TRUE;
    st.pInputSurface = iv;

    unsigned long frames = 0;
    for (;;) {
        int r = read_full(stdin, inBuf, inSize);
        if (r == 0) break;
        if (r < 0) { fprintf(stderr, "ForgeRTX: truncated frame at end of input\n"); break; }

        D3D11_MAPPED_SUBRESOURCE m;
        if (FAILED(hr = ID3D11DeviceContext_Map(ctx, (ID3D11Resource *)inStage, 0, D3D11_MAP_WRITE, 0, &m))) die("Map(input)", hr);
        unsigned char *d = (unsigned char *)m.pData;
        for (int y = 0; y < h; y++) memcpy(d + (size_t)y * m.RowPitch, inBuf + (size_t)y * w, w);              /* Y  */
        unsigned char *uv = d + (size_t)m.RowPitch * h;
        for (int y = 0; y < h / 2; y++) memcpy(uv + (size_t)y * m.RowPitch, inBuf + (size_t)w * h + (size_t)y * w, w); /* UV */
        ID3D11DeviceContext_Unmap(ctx, (ID3D11Resource *)inStage, 0);
        ID3D11DeviceContext_CopyResource(ctx, (ID3D11Resource *)inTex, (ID3D11Resource *)inStage);

        st.InputFrameOrField = frames;
        if (FAILED(hr = ID3D11VideoContext_VideoProcessorBlt(vctx, vp, ov, frames, 1, &st))) die("VideoProcessorBlt", hr);

        ID3D11DeviceContext_CopyResource(ctx, (ID3D11Resource *)outStage, (ID3D11Resource *)outTex);
        if (FAILED(hr = ID3D11DeviceContext_Map(ctx, (ID3D11Resource *)outStage, 0, D3D11_MAP_READ, 0, &m))) die("Map(output)", hr);
        unsigned char *s = (unsigned char *)m.pData;
        for (int y = 0; y < oh; y++) memcpy(outBuf + (size_t)y * outRow, s + (size_t)y * m.RowPitch, outRow);
        ID3D11DeviceContext_Unmap(ctx, (ID3D11Resource *)outStage, 0);
        if (fwrite(outBuf, 1, outRow * oh, stdout) != outRow * oh) break;   /* encoder closed */
        frames++;
    }
    fflush(stdout);
    fprintf(stderr, "ForgeRTX: %lu frame(s) processed\n", frames);
    return 0;
}
