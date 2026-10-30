namespace Nakahira
{
    // CPU の状態(積む・逃げる・攻めるなど)の基本形
    // 決める側(DarumaCpu)は、各状態の Evaluate(やりたさ)を比べて一番高い状態にする
    public abstract class CpuState
    {
        // 表示・デバッグ用の名前
        public abstract string Name { get; }

        // やりたさ(0〜1)。今この状態でなければ、この強さで移りたがる。今の状態なら、続けたい強さ
        public abstract float Evaluate(CpuContext c);

        // しきい値(集中度＝疲れの割合)。疲れの割合がこれより高いと、この状態へは移れない(今この状態なら続けられる)
        public virtual float Threshold(CpuContext c) => float.MaxValue;

        public virtual void Enter(CpuContext c) { }
        public virtual void Exit(CpuContext c) { }

        // 操作を cmd に書く。状態が終わったら false(すぐに選び直す)
        public abstract bool Tick(CpuContext c, ref DarumaCommand cmd);

        // 今たどっている道(なければ null)。移動床に乗って行く道なら、乗り降りを CpuRide が行う
        public virtual CpuPathFollower Route => null;

        public virtual void DrawGizmos(CpuContext c) { }
    }
}
