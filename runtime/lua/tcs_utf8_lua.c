/* TinyC# の文字列の操作 (runtime/utf8) を Lua の table として出す module。
   tinysystem.lua は global の tcs_utf8 を呼ぶので、Lua を動かす host は VM を作るときに登録する:
     luaL_requiref(L, "tcs_utf8", luaopen_tcs_utf8, 1); lua_pop(L, 1);
   runtime/utf8 の C と一緒に build する。 */
#include "lauxlib.h"
#include "lua.h"

#include "../utf8/tcs_utf8.h"

static int tcs_utf8_l_rune_count(lua_State *L) {
  size_t length;
  const char *s = luaL_checklstring(L, 1, &length);
  lua_pushinteger(L, tcs_utf8_rune_count((const unsigned char *)s, length));
  return 1;
}

static int tcs_utf8_l_rune_substring(lua_State *L) {
  size_t length, from, to;
  const char *s = luaL_checklstring(L, 1, &length);
  if (!tcs_utf8_rune_range((const unsigned char *)s, length,
                           (int32_t)luaL_checkinteger(L, 2),
                           (int32_t)luaL_checkinteger(L, 3), &from, &to))
    return luaL_error(L, "RuneSubstring: range is out of the string");
  lua_pushlstring(L, s + from, to - from);
  return 1;
}

/* scrub(s, offset, length): s の [offset, offset + length) の壊れた並びを U+FFFD にした string */
static int tcs_utf8_l_scrub(lua_State *L) {
  const unsigned char *s = (const unsigned char *)luaL_checkstring(L, 1);
  const unsigned char *data = s + luaL_checkinteger(L, 2);
  size_t length = (size_t)luaL_checkinteger(L, 3);
  size_t total = tcs_utf8_scrub_length(data, length);
  luaL_Buffer buffer;
  tcs_utf8_scrub(data, length, (unsigned char *)luaL_buffinitsize(L, &buffer, total));
  luaL_pushresultsize(&buffer, total);
  return 1;
}

static int tcs_utf8_l_trim(lua_State *L) {
  size_t length, from, to;
  const char *s = luaL_checklstring(L, 1, &length);
  tcs_utf8_trim((const unsigned char *)s, length, &from, &to);
  lua_pushlstring(L, s + from, to - from);
  return 1;
}

/* find_white_space(s, pos): pos (1 始まり) 以降で最初の空白 1 文字の位置 (string.find と同じ形)。無ければ nil */
static int tcs_utf8_l_find_white_space(lua_State *L) {
  size_t length, start, stop;
  const char *s = luaL_checklstring(L, 1, &length);
  if (!tcs_utf8_find_white_space((const unsigned char *)s, length,
                                 (size_t)luaL_checkinteger(L, 2) - 1, &start, &stop))
    return 0;
  lua_pushinteger(L, (lua_Integer)start + 1);
  lua_pushinteger(L, (lua_Integer)stop);
  return 2;
}

static const luaL_Reg tcs_utf8_funcs[] = {
    {"rune_count", tcs_utf8_l_rune_count},
    {"rune_substring", tcs_utf8_l_rune_substring},
    {"scrub", tcs_utf8_l_scrub},
    {"trim", tcs_utf8_l_trim},
    {"find_white_space", tcs_utf8_l_find_white_space},
    {NULL, NULL}};

LUAMOD_API int luaopen_tcs_utf8(lua_State *L) {
  luaL_newlib(L, tcs_utf8_funcs);
  return 1;
}
