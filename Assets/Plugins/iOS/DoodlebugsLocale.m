// DoodlebugsLocale.m
// Tells Loc.cs which UI language iOS picked for the app. Matching the user's
// language list against CFBundleLocalizations (written by
// iOSPostProcessBuild from Loc.RuntimeLanguages) honours both the system
// order and the per-app language chosen in Settings.

#import <Foundation/Foundation.h>
#include <stdlib.h>
#include <string.h>

const char *_DoodlebugsPreferredLanguage(void)
{
    @autoreleasepool {
        NSArray<NSString *> *supported =
            [[NSBundle mainBundle] objectForInfoDictionaryKey:@"CFBundleLocalizations"];
        NSString *language = nil;
        if ([supported isKindOfClass:[NSArray class]] && supported.count > 0) {
            language = [NSBundle preferredLocalizationsFromArray:supported
                                                  forPreferences:[NSLocale preferredLanguages]].firstObject;
        }
        if (language == nil) language = [NSLocale preferredLanguages].firstObject;
        if (language == nil) return NULL;
        // Mono frees the returned buffer, so it must come from malloc.
        return strdup(language.UTF8String);
    }
}
