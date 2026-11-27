namespace UniDAW.Composer
{
    /// <summary>
    /// 再生先の抽象。コンポーザーはこれ越しにエンジンへ触るので、
    /// エディタの隠しプレビューホスト以外（シーン上のプレイヤー、テスト用スタブ等）にも差し替えられる。
    /// </summary>
    public interface IAudioHost
    {
        ChipEngine Engine { get; }
        /// <summary>オーディオ出力を動かす（止まっていれば再開）。</summary>
        void EnsureRunning();
        /// <summary>エンジンからのノートイベント等をメインスレッドで配る。</summary>
        void DispatchEvents();
    }
}
