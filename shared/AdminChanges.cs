#if NET8_0_OR_GREATER
#nullable disable
#endif
using System;
using System.Linq;
namespace Qchat.GameAdmin
{
    public static class AdminChanges
    {
        public static AdminSnapshot Apply(AdminSnapshot current, AdminRequest request)
        {
            var next = AdminJson.Clone(current);
            switch (request.Operation)
            {
                case "create-group":
                    if (request.Group == null || next.Groups.Any(g => g.Key == request.Group.Key)) throw new InvalidOperationException("权限组已存在或数据为空");
                    request.Group.Shared = false; request.Group.PluginOnly = false;
                    next.Groups.Add(request.Group); break;
                case "update-group":
                    var group = next.Groups.FirstOrDefault(g => g.Key == request.Key);
                    if (group == null || request.Group == null || request.Group.Key != request.Key) throw new InvalidOperationException("组不存在或标识发生改变");
                    if (group.Shared) throw new InvalidOperationException("此组来自游戏共享配置，请先复制为本服权限组");
                    request.Group.Shared = false; request.Group.PluginOnly = group.PluginOnly;
                    next.Groups[next.Groups.IndexOf(group)] = request.Group; break;
                case "delete-group":
                    if (next.Members.Any(m => m.Group == request.Key) || next.Groups.Any(g => g.Inheritance.Contains(request.Key))) throw new InvalidOperationException("组仍有成员或继承引用，请先调整引用");
                    if (next.Groups.Any(g => g.Key == request.Key && (g.Shared || g.PluginOnly))) throw new InvalidOperationException("不能删除共享组或框架默认权限组");
                    if (next.Groups.RemoveAll(g => g.Key == request.Key) == 0) throw new InvalidOperationException("权限组不存在");
                    break;
                case "set-member":
                    if (request.Member == null) throw new InvalidOperationException("管理员数据为空");
                    if (next.Members.Any(m => m.UserId == request.Member.UserId && m.Shared)) throw new InvalidOperationException("此管理员来自共享配置，请在共享配置中调整");
                    if (next.Groups.Any(g => g.Key == request.Member.Group && (g.PluginOnly || g.Shared))) throw new InvalidOperationException("请选择本服游戏权限组");
                    request.Member.Shared = false;
                    next.Members.RemoveAll(m => m.UserId == request.Member.UserId); next.Members.Add(request.Member); break;
                case "delete-member":
                    if (next.Members.Any(m => m.UserId == request.Key && m.Shared)) throw new InvalidOperationException("不能删除共享管理员");
                    if (next.Members.RemoveAll(m => m.UserId == request.Key) == 0) throw new InvalidOperationException("管理员不存在"); break;
                default: throw new InvalidOperationException("不支持的权限操作");
            }
            AdminValidation.Validate(next);
            return next;
        }
    }
}
