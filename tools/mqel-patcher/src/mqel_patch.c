// MQEL client cert-bypass — dinput8.dll proxy (DLL-hijack injection).
//
// Dropped into GameData\Bin, this loads automatically inside MightyQuest.exe (dinput8 loads only in
// the main game process, never the CEF UI subprocess), forwards all 6 real dinput8 exports to
// dinput8_orig.dll, and applies the two-byte TLS-verify bypass so the client accepts our server's
// certificate. No external launcher script needed.
//
// The game statically imports DirectInput8Create from dinput8, so we must provide the real thing. This
// proxy ships ONLY our own code: the exports are forwarded (via src/dinput8.def) to dinput8_orig.dll,
// which the PATCHER creates by copying the user's OWN Windows\System32\dinput8.dll into Bin. (A renamed
// companion is required — forwarding to "dinput8.dll" would collide with our own base name and recurse.)
// Nothing outside the game folder is modified; the OS dinput8 is only read once, to make that local copy.
//
//   patch #1 @ RVA 0x6219FE : 0F 95 C2 (setnz dl) -> 90 90 90  => SSL_VERIFY_NONE
//   patch #2 @ RVA 0x622FD6 : 74 (JZ)             -> EB         => step3 always "verify ok"
//
// TIMING (hardened, machine-independent):
//   The game runs a one-time .text integrity check early in boot; patching BEFORE it makes the check
//   detect the tamper and sabotage the cert. So we must patch strictly AFTER it. Instead of a fixed
//   wall-clock wait (fragile: too early on slow machines, too late on fast ones where the TLS retries
//   exhaust), we key the patch to the client's OWN TLS activity: we watch Bin\NetworkLog.txt and patch
//   the moment the first TLS handshake appears. Networking starts well after boot, so this is always
//   after the integrity check, and it adapts to any machine speed. The client's handshake retries then
//   pick up the patched verify. A generous timeout falls back to patching anyway.

#include <windows.h>

#define RVA1 0x6219FE
#define RVA2 0x622FD6
#define UNPACK_TIMEOUT_MS   90000   // max wait for the verify code to unpack
#define TLS_TIMEOUT_MS      30000   // max wait for TLS activity after unpack, then patch anyway
#define POLL_MS             50

static void logline(const char* msg){
  char tp[MAX_PATH]; GetTempPathA(MAX_PATH, tp); lstrcatA(tp, "mqel-patch.log");
  HANDLE f = CreateFileA(tp, FILE_APPEND_DATA, FILE_SHARE_READ|FILE_SHARE_WRITE, 0, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, 0);
  if(f != INVALID_HANDLE_VALUE){ DWORD w; WriteFile(f, msg, lstrlenA(msg), &w, 0); CloseHandle(f); }
}

// Bin\NetworkLog.txt (same directory as MightyQuest.exe)
static void netlog_path(char* out){
  GetModuleFileNameA(NULL, out, MAX_PATH);
  char* slash = out; char* p;
  for(p = out; *p; p++){ if(*p == '\x5c' || *p == '/') slash = p; }
  *(slash + 1) = 0;
  lstrcatA(out, "NetworkLog.Txt");
}

// NetworkLog.Txt size captured at patch start. The game APPENDS to this log across sessions, so it can
// carry stale "handshake" lines from previous runs. If we react to those we patch BEFORE the one-time
// integrity check runs and it flags "file corruption". So we only scan bytes written AFTER g_baseline —
// i.e. THIS session's networking (which starts after the check). See patch_thread.
static DWORD g_baseline = 0;

static DWORD netlog_size(const char* path){
  HANDLE f = CreateFileA(path, GENERIC_READ, FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,
                         0, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, 0);
  if(f == INVALID_HANDLE_VALUE) return 0;
  DWORD s = GetFileSize(f, 0);
  CloseHandle(f);
  return (s == INVALID_FILE_SIZE) ? 0 : s;
}

// true once THIS session's network log shows a TLS handshake (client reached networking, after the check)
static int tls_started(const char* path){
  HANDLE f = CreateFileA(path, GENERIC_READ, FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,
                         0, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, 0);
  if(f == INVALID_HANDLE_VALUE) return 0;
  int found = 0;
  DWORD size = GetFileSize(f, 0);
  if(size != INVALID_FILE_SIZE && size > 0){
    // scan only the region written since we started: [from, size). If the game recreated/truncated the
    // file (size shrank below baseline), it's a fresh file this session -> scan from 0.
    DWORD from = (size < g_baseline) ? 0 : g_baseline;
    DWORD len = size - from;
    if(len > 65536) len = 65536;   // first 64KB of the new region — the handshake is the first TLS line
    if(len > 0){
      SetFilePointer(f, from, 0, FILE_BEGIN);
      char* buf = (char*)HeapAlloc(GetProcessHeap(), 0, len + 1);
      if(buf){
        DWORD got = 0;
        if(ReadFile(f, buf, len, &got, 0) && got){
          buf[got] = 0;
          for(DWORD i = 0; i + 9 <= got; i++){
            if(buf[i]=='h'&&buf[i+1]=='a'&&buf[i+2]=='n'&&buf[i+3]=='d'&&buf[i+4]=='s'&&
               buf[i+5]=='h'&&buf[i+6]=='a'&&buf[i+7]=='k'&&buf[i+8]=='e'){ found = 1; break; }
          }
        }
        HeapFree(GetProcessHeap(), 0, buf);
      }
    }
  }
  CloseHandle(f);
  return found;
}

static void apply_patch(BYTE* a1, BYTE* a2){
  DWORD old;
  VirtualProtect(a1, 3, PAGE_EXECUTE_READWRITE, &old);
  a1[0] = 0x90; a1[1] = 0x90; a1[2] = 0x90;
  VirtualProtect(a1, 3, old, &old);
  VirtualProtect(a2, 1, PAGE_EXECUTE_READWRITE, &old);
  a2[0] = 0xEB;
  VirtualProtect(a2, 1, old, &old);
  FlushInstructionCache(GetCurrentProcess(), a1, 3);
  FlushInstructionCache(GetCurrentProcess(), a2, 1);
}

static DWORD WINAPI patch_thread(LPVOID unused){
  // only patch the main game process
  char host[MAX_PATH]; GetModuleFileNameA(NULL, host, MAX_PATH);
  char* base = host; char* p;
  for(p = host; *p; p++){ if(*p == '\x5c' || *p == '/') base = p + 1; }
  if(lstrcmpiA(base, "MightyQuest.exe") != 0) return 0;

  BYTE* mod = (BYTE*)GetModuleHandleA(NULL);
  BYTE* a1 = mod + RVA1;
  BYTE* a2 = mod + RVA2;

  // Record the network log's current size NOW, before the game does any networking. Everything already
  // in the file is stale (previous sessions) and must be ignored — otherwise a stale "handshake" would
  // make us patch before the integrity check and trip "file corruption". We captured this as early as
  // possible: dinput8 loads during the process's initial DLL load, long before networking starts.
  { char nl0[MAX_PATH]; netlog_path(nl0); g_baseline = netlog_size(nl0); }

  // 1. wait for the verify code to be unpacked at a1 (0F 95 C2)
  int waited = 0;
  while(!(a1[0]==0x0F && a1[1]==0x95 && a1[2]==0xC2)){
    Sleep(POLL_MS); waited += POLL_MS;
    if(waited >= UNPACK_TIMEOUT_MS){ logline("FAIL: verify bytes never unpacked\r\n"); return 0; }
  }

  // 2. wait for the client's first TLS handshake (guaranteed after the one-time integrity check),
  //    or fall back after TLS_TIMEOUT_MS
  char nl[MAX_PATH]; netlog_path(nl);
  int tls_wait = 0, via_tls = 0;
  for(;;){
    if(tls_started(nl)){ via_tls = 1; break; }
    Sleep(POLL_MS); tls_wait += POLL_MS;
    if(tls_wait >= TLS_TIMEOUT_MS) break;
  }

  // 3. patch (bytes may have been re-unpacked; re-verify)
  if(!(a1[0]==0x0F && a1[1]==0x95 && a1[2]==0xC2)){ logline("FAIL: verify bytes changed before patch\r\n"); return 0; }
  apply_patch(a1, a2);
  logline(via_tls ? "OK: cert patch applied (TLS-triggered)\r\n" : "OK: cert patch applied (timeout fallback)\r\n");

  // 4. defensive: if the region ever reverts to original, re-apply (belt-and-suspenders)
  int i;
  for(i = 0; i < 200; i++){
    Sleep(100);
    if(a1[0]==0x0F && a1[1]==0x95 && a1[2]==0xC2){ apply_patch(a1, a2); logline("re-applied cert patch\r\n"); }
  }
  return 0;
}

BOOL WINAPI DllMain(HINSTANCE h, DWORD reason, LPVOID reserved){
  if(reason == DLL_PROCESS_ATTACH){
    DisableThreadLibraryCalls(h);
    CreateThread(0, 0, patch_thread, 0, 0, 0);
  }
  return TRUE;
}

// The 6 real dinput8 exports are re-exported as forwarders to dinput8_orig.dll (see src/dinput8.def).
// A RENAMED companion is mandatory: a proxy DLL can't forward to "dinput8.dll" by path, because a module
// with that base name (ours) is already loaded, so the loader would resolve the forward back to us and
// recurse. The patcher creates dinput8_orig.dll by copying the user's own System32\dinput8.dll into Bin.
