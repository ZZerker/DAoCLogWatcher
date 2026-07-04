using System.Collections.Generic;
using DAoCLogWatcher.UI.Models;

namespace DAoCLogWatcher.UI.Services;

public interface ISessionHistoryService
{
	IReadOnlyList<SessionRecord> Load();

	void Upsert(SessionRecord record);
}
