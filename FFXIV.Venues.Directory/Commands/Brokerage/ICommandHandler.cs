using System.Threading.Tasks;

namespace FFXIV.Venues.Directory.Commands.Brokerage
{
    internal interface ICommandHandler
    {
        Task Handle(string args);
    }
}

