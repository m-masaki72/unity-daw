using System;

namespace UniDAW
{
    /// <summary>
    /// 条件トリガー（A:B）とプロバビリティの判定。乱数は位置からのハッシュなので、
    /// 同じシード・同じ位置なら毎回同じ結果になる（オフライン書き出しとプレビューが一致する）。
    /// </summary>
    internal static class TriggerConditions
    {
        /// <param name="cycle">そのパターンの何周目（何度目の出現）か</param>
        /// <param name="idx">トラック内のセル位置</param>
        /// <param name="pi">パターン番号</param>
        public static bool Passes(in Cell cell, uint seed, int channel, int cycle, int idx, int pi)
        {
            if (cell.condB > 1)
            {
                int a = Math.Max(1, Math.Min(cell.condB, cell.condA));
                if (cycle % cell.condB != a - 1) return false;
            }
            if (cell.probability < 100)
            {
                if (cell.probability <= 0) return false;
                uint h = Hash(seed, (uint)cycle, (uint)idx, (uint)(channel * 1024 + pi));
                if (h % 100u >= (uint)cell.probability) return false;
            }
            return true;
        }

        static uint Hash(uint a, uint b, uint c, uint d)
        {
            uint h = a ^ 0x85EBCA6Bu;
            h = (h ^ b) * 0x9E3779B1u;
            h = (h ^ (h >> 15) ^ c) * 0x85EBCA77u;
            h = (h ^ (h >> 13) ^ d) * 0xC2B2AE3Du;
            return h ^ (h >> 16);
        }
    }
}
