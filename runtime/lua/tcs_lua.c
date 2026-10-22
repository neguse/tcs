/* tcs のテストと perf が使う lua / lua32 の interpreter。
   標準の lua.c に、global の tcs_math (runtime/lua/tcs_math_lua.c) と tcs_rt (runtime/lua/tcs_rt_lua.c) の登録と、
   環境変数 TCS_LUA_SEED があるときだけ string の hash seed を固定する state 生成を足したもの。
   seed を固定すると Dictionary の pairs の順が実行間で同じになり、落ちた fuzz や差分テストを同じ状態で再現できる
   (列挙順は未規定のままで、perf / samples / host は Lua 既定の seed。q.md Q18)。 */
#include <stdlib.h>

#include "lauxlib.h"
#include "lua.h"
#include "lualib.h"

static lua_State *tcs_newstate(void) {
  const char *seed = getenv("TCS_LUA_SEED");
  if (seed == NULL) return luaL_newstate();
  return lua_newstate(luaL_alloc, NULL, (unsigned)strtoul(seed, NULL, 0));
}

LUAMOD_API int luaopen_tcs_math(lua_State *L);
LUAMOD_API int luaopen_tcs_rt(lua_State *L);

static void tcs_openlibs(lua_State *L) {
  luaL_openlibs(L);
  luaL_requiref(L, "tcs_math", luaopen_tcs_math, 1);
  lua_pop(L, 1);
  luaL_requiref(L, "tcs_rt", luaopen_tcs_rt, 1);
  lua_pop(L, 1);
}

#define luai_openlibs(L) tcs_openlibs(L)
#define luaL_newstate() tcs_newstate()
#include "lua.c"
