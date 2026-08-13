#pragma once
#ifndef ES_CORE_RESOURCES_PROTECTED_DECORATIONS_H
#define ES_CORE_RESOURCES_PROTECTED_DECORATIONS_H

#include <cstddef>
#include <memory>
#include <string>

namespace ProtectedDecorations
{
	bool isResourcePath(const std::string& path);
	bool hasSystem(const std::string& systemName);
	std::string resourcePathForSystem(const std::string& systemName);
	bool loadResource(const std::string& path,
		std::shared_ptr<unsigned char>& data, size_t& length);
}

#endif // ES_CORE_RESOURCES_PROTECTED_DECORATIONS_H
