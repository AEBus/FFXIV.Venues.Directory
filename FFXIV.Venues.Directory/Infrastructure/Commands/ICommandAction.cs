using System.Threading.Tasks;

namespace FFXIV.Venues.Directory.Infrastructure.Commands
{
    internal interface ICommandAction
    {
        Task Handle(string args);
    }
}
