namespace AlicizaX
{
    // public interface IProcedure
    // {
    //     void Init();
    //     void Enter();
    //     void Leave();
    //     void Update();
    //     void Destroy();
    // }

    /// <summary>
    /// 流程基类 - 使用模板方法模式定义流程生命周期
    /// </summary>
    public abstract class ProcedureBase
    {
        protected internal virtual void OnInit()
        {
        }

        protected internal virtual void OnEnter()
        {
        }

        protected internal virtual void OnLeave()
        {
        }

        protected internal virtual void OnUpdate()
        {
        }

        protected internal virtual void OnDestroy()
        {
        }


        protected internal void SwitchProcedure<T>() where T : ProcedureBase
        {
            ProcedureBuilder.SwitchProcedure<T>();
        }
    }
}
