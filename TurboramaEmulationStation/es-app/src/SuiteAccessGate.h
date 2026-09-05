#pragma once

#include <memory>
#include <string>

// The Suite edition admits the frontend only while its pinned licensing
// process proves a live server session over private inherited pipes.
class SuiteAccessGate final
{
public:
	static SuiteAccessGate& instance();
	~SuiteAccessGate();
	bool start(std::string& error);
	bool authorized() const;
	void stop();
	static bool runSelfTest();
	static bool verifyHelperIntegrity();
	// Local diagnostic only: 0 = existing Suite identity, 21 = unavailable,
	// 44 = embedded payload/extraction/process failure. Never activates/signs.
	static int probeIdentity();

	SuiteAccessGate(const SuiteAccessGate&) = delete;
	SuiteAccessGate& operator=(const SuiteAccessGate&) = delete;

private:
	SuiteAccessGate();
	struct State;
	std::unique_ptr<State> mState;
};
