#pragma once

#include <string>

namespace GameSplash
{
	enum class Kind
	{
		ENTRY,
		EXIT
	};

	enum class MediaType
	{
		IMAGE,
		VIDEO
	};

	struct Media
	{
		std::string path;
		MediaType type = MediaType::IMAGE;

		bool valid() const { return !path.empty(); }
	};

	Media resolve(const std::string& systemName, Kind kind);
}